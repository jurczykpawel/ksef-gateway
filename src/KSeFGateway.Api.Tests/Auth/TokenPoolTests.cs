using System.Text.Json;
using KSeF.Client.Core.Models.Authorization;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Licensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Auth;

public class TokenPoolTests
{
    private const string Nip = "1234567890";

    [Fact]
    public async Task ConcurrentLazyAndRefresh_PublishOneConsistentState()
    {
        using var pool = new TestPool();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.Authenticate = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var lazy = pool.GetAccessTokenAsync(Nip);
        await entered.Task;
        await pool.StartAsync(default); // Eager auth competes with the in-flight lazy auth.
        var readers = Enumerable.Range(0, 32).Select(_ => pool.GetAccessTokenAsync(Nip)).ToArray();
        var refresh = pool.Refresh();
        release.SetResult();
        try
        {
            await Task.WhenAll(readers.Append(lazy));
            await refresh;
            Assert.Equal(1, pool.AuthCalls);
            Assert.All(readers, r => Assert.Equal("access-1", r.Result));
            var original = pool.GetState(Nip);
            Assert.Equal("refresh-1", original.RefreshToken);
            Assert.Equal(pool.Clock.Now.AddMinutes(10), original.AccessTokenExpiresAt);
            pool.Clock.Advance(TimeSpan.FromMinutes(8));
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => pool.Refresh()));
            Assert.Equal(1, pool.RefreshCalls);
            Assert.Equal("access-1", original.AccessToken); // Previously returned snapshots stay unchanged.
            Assert.Equal("renewed-1", pool.GetState(Nip).AccessToken);
            Assert.Equal("refresh-1", pool.GetState(Nip).RefreshToken);
        }
        finally { await pool.StopAsync(default); }
    }

    [Fact]
    public async Task Refresh_PublishesNewSnapshotWithoutMutatingOldState()
    {
        using var pool = new TestPool();
        await pool.GetAccessTokenAsync(Nip);
        var original = pool.GetState(Nip);
        pool.Clock.Advance(TimeSpan.FromMinutes(8));
        await pool.Refresh();
        Assert.NotSame(original, pool.GetState(Nip));
        Assert.Equal("access-1", original.AccessToken);
        Assert.Equal("refresh-1", original.RefreshToken);
        Assert.Equal("renewed-1", pool.GetState(Nip).AccessToken);
        Assert.Equal("refresh-1", pool.GetState(Nip).RefreshToken);
    }

    [Fact]
    public async Task InFlightRefresh_ExpiredLazyReadersWaitForCompleteSnapshot()
    {
        using var pool = new TestPool();
        await pool.GetAccessTokenAsync(Nip);
        var original = pool.GetState(Nip);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.Refreshing = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        pool.Clock.Advance(TimeSpan.FromMinutes(8));
        var refresh = pool.Refresh();
        await entered.Task;
        Assert.Same(original, pool.GetState(Nip));
        pool.Clock.Advance(TimeSpan.FromMinutes(2));
        var readers = Enumerable.Range(0, 32).Select(_ => pool.GetAccessTokenAsync(Nip)).ToArray();
        var allWaiting = readers.All(r => !r.IsCompleted);
        release.SetResult();
        await refresh;
        await Task.WhenAll(readers);
        Assert.True(allWaiting, "Expired lazy readers must wait for the in-flight refresh");
        Assert.Equal(1, pool.AuthCalls);
        Assert.All(readers, r => Assert.Equal("renewed-1", r.Result));
        Assert.Equal("access-1", original.AccessToken);
        Assert.Equal("refresh-1", pool.GetState(Nip).RefreshToken);
    }

    [Fact]
    public async Task EagerCancellation_Propagates()
    {
        using var pool = new TestPool();
        pool.Authenticate = _ => throw new OperationCanceledException("eager cancelled");
        // .NET 10 runs BackgroundService.ExecuteAsync on its own task, so StartAsync no longer surfaces a
        // synchronous failure; the cancellation must be observable on ExecuteTask (and must not be swallowed).
        await pool.StartAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.ExecuteTask!);
    }

    [Fact]
    public async Task LazyAuthenticationFailure_ReturnsNullAndCanRetry()
    {
        using var pool = new TestPool();
        pool.Authenticate = _ => throw new HttpRequestException("temporary auth outage");
        Assert.Null(await pool.GetAccessTokenAsync(Nip));
        Assert.Null(pool.GetState(Nip).AccessToken);
        pool.Authenticate = null;
        Assert.Equal("access-2", await pool.GetAccessTokenAsync(Nip));
        Assert.Equal(2, pool.AuthCalls);
    }

    [Fact]
    public async Task RefreshTransientFailure_KeepsUnexpiredAccessToken()
    {
        using var pool = new TestPool();
        await pool.GetAccessTokenAsync(Nip);
        var original = pool.GetState(Nip);
        pool.Clock.Advance(TimeSpan.FromMinutes(8));
        pool.RefreshError = new HttpRequestException("temporary outage");
        await pool.Refresh();
        Assert.Equal(1, pool.RefreshCalls);
        Assert.Same(original, pool.GetState(Nip));
        Assert.Equal("access-1", await pool.GetAccessTokenAsync(Nip));
        Assert.Equal(1, pool.AuthCalls);
        pool.RefreshError = null;
        await pool.Refresh();
        Assert.Equal("renewed-2", pool.GetState(Nip).AccessToken);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public async Task Refresh_UsesActualLifetime(int minutes)
    {
        using var pool = new TestPool { Lifetime = TimeSpan.FromMinutes(minutes) };
        await pool.GetAccessTokenAsync(Nip);
        pool.Clock.Advance(pool.Lifetime * 0.8 - TimeSpan.FromTicks(1));
        await pool.Refresh();
        Assert.Equal(0, pool.RefreshCalls);
        pool.Clock.Advance(TimeSpan.FromTicks(1));
        await pool.Refresh();
        Assert.Equal(1, pool.RefreshCalls);
        Assert.Equal("renewed-1", pool.GetState(Nip).AccessToken);
        pool.Clock.Advance(pool.Lifetime * 0.8 - TimeSpan.FromTicks(1));
        await pool.Refresh();
        Assert.Equal(1, pool.RefreshCalls);
        pool.Clock.Advance(TimeSpan.FromTicks(1));
        await pool.Refresh();
        Assert.Equal(2, pool.RefreshCalls);
    }

    [Fact]
    public async Task LazyCancellation_Propagates()
    {
        using var pool = new TestPool();
        pool.Authenticate = _ => throw new OperationCanceledException("auth cancelled");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.GetAccessTokenAsync(Nip));
        Assert.Null(pool.GetState(Nip).AccessToken);
    }

    [Fact]
    public async Task RefreshCancellation_PropagatesAndKeepsSnapshot()
    {
        using var pool = new TestPool();
        await pool.GetAccessTokenAsync(Nip);
        var original = pool.GetState(Nip);
        pool.Clock.Advance(TimeSpan.FromMinutes(8));
        pool.RefreshError = new OperationCanceledException("refresh cancelled");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.Refresh());
        Assert.Same(original, pool.GetState(Nip));
    }

    [Fact]
    public async Task ExpiredAccessAndRefresh_Reauthenticates()
    {
        using var pool = new TestPool();
        await pool.GetAccessTokenAsync(Nip);
        pool.Clock.Advance(TimeSpan.FromDays(2));
        await pool.Refresh();
        Assert.Equal(2, pool.AuthCalls);
        Assert.Equal(0, pool.RefreshCalls);
        Assert.Equal("access-2", await pool.GetAccessTokenAsync(Nip));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan duration) => Now += duration;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TestPool : TokenPool
    {
        public Clock Clock { get; }
        public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(10);
        public int AuthCalls;
        public int RefreshCalls;
        public Func<CancellationToken, Task>? Authenticate;
        public Exception? RefreshError;
        public Func<CancellationToken, Task>? Refreshing;
        public TestPool() : this(new Clock()) { }
        private TestPool(Clock clock) : base(new ServiceCollection().BuildServiceProvider(), Provider(),
            NullLogger<TokenPool>.Instance, clock) => Clock = clock;
        public Task Refresh(CancellationToken ct = default) => RefreshActiveContextsAsync(ct);
        protected override async Task<AuthenticationOperationStatusResponse> AuthenticateAsync(string nip, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref AuthCalls);
            if (Authenticate != null) await Authenticate(ct);
            return JsonSerializer.Deserialize<AuthenticationOperationStatusResponse>(JsonSerializer.Serialize(new
            {
                AccessToken = new { Token = $"access-{call}", ValidUntil = Clock.Now + Lifetime },
                RefreshToken = new { Token = $"refresh-{call}", ValidUntil = Clock.Now.AddDays(1) }
            }))!;
        }
        protected override async Task<(string Token, DateTimeOffset ValidUntil)> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref RefreshCalls);
            if (Refreshing != null) await Refreshing(ct);
            if (RefreshError != null) throw RefreshError;
            Assert.Equal("refresh-1", refreshToken);
            return ($"renewed-{call}", Clock.Now + Lifetime);
        }
    }

    private static ContextProvider Provider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KSEF_CONTEXTS_FILE"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
            ["KSEF_NIP"] = Nip, ["KSEF_TOKEN"] = "fake"
        }).Build();
        using var http = new HttpClient(new RejectNetwork());
        var license = new LicenseService(new JwksClient(http, "https://unused.invalid", NullLogger<JwksClient>.Instance),
            new RevocationClient(http, "https://unused.invalid", NullLogger<RevocationClient>.Instance), config,
            NullLogger<LicenseService>.Instance);
        return new ContextProvider(config, NullLogger<ContextProvider>.Instance, license);
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Tests must not access the network");
    }
}
