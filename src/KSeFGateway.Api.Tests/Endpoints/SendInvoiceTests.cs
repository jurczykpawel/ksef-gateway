using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Sessions;
using KSeF.Client.Core.Models.Sessions.OnlineSession;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Middleware;
using KSeFGateway.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KSeFGateway.Api.Tests.Endpoints;

public class SendInvoiceTests
{
    private readonly Mock<IKSeFClient> client = new();
    private readonly Mock<ICryptographyService> crypto = new();
    private readonly Clock clock = new();
    private readonly CancellationTokenSource cancellation = new();
    private int polls;
    private int closes;
    private OpenOnlineSessionRequest? opened;
    private readonly List<CancellationToken> tokens = [];
    private Exception? closeError;

    public SendInvoiceTests()
    {
        crypto.Setup(x => x.GetEncryptionData()).Returns(new EncryptionData
        {
            CipherKey = new byte[32], CipherIv = new byte[16],
            EncryptionInfo = new() { EncryptedSymmetricKey = "key", InitializationVector = "iv", PublicKeyId = new string('A', 43) + "=" }
        });
        crypto.Setup(x => x.EncryptBytesWithAES256(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>())).Returns(new byte[16]);
        crypto.Setup(x => x.GetMetaData(It.IsAny<byte[]>())).Returns(new FileMetadata { HashSHA = "hash", FileSize = 16 });
        client.Setup(x => x.OpenOnlineSessionAsync(It.IsAny<OpenOnlineSessionRequest>(), "token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<OpenOnlineSessionRequest, string, string, CancellationToken>((r, _, _, ct) => { opened = r; tokens.Add(ct); })
            .ReturnsAsync(new OpenOnlineSessionResponse { ReferenceNumber = "session" });
        client.Setup(x => x.SendOnlineSessionInvoiceAsync(It.IsAny<SendInvoiceRequest>(), "session", "token", It.IsAny<CancellationToken>()))
            .Callback<SendInvoiceRequest, string, string, CancellationToken>((_, _, _, ct) => tokens.Add(ct))
            .ReturnsAsync(new SendInvoiceResponse { ReferenceNumber = "invoice" });
        client.Setup(x => x.CloseOnlineSessionAsync("session", "token", It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, _, ct) => { closes++; tokens.Add(ct); return closeError is null ? Task.CompletedTask : Task.FromException(closeError); });
        Status(() => Invoice(200, "accepted", "KSEF-123"));
    }

    private static SessionInvoice Invoice(int code, string? description = null, string? number = null) =>
        new() { Status = new InvoiceStatusInfo { Code = code, Description = description! }, KsefNumber = number! };

    private void Status(Func<SessionInvoice> status) =>
        client.Setup(x => x.GetSessionInvoiceAsync("session", "invoice", "token", It.IsAny<CancellationToken>()))
            .Returns<string, string, string, CancellationToken>((_, _, _, ct) => { polls++; tokens.Add(ct); return Task.FromResult(status()); });

    private async Task<IResult> Send()
    {
        var method = typeof(WorkflowEndpoints).GetMethod("SendInvoiceXml", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            return await (Task<IResult>)method.Invoke(null, ["<Faktura/>", "token", client.Object, crypto.Object,
                NullLogger.Instance, cancellation.Token, clock, (Func<TimeSpan, CancellationToken, Task>)((duration, ct) =>
                { ct.ThrowIfCancellationRequested(); clock.Advance(duration); return Task.CompletedTask; })])!;
        }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }

    private static ApiResponse Body(IResult result) => Assert.IsType<ApiResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public async Task Send_ProcessingDescription_ContinuesPolling(int code)
    {
        Status(() => polls == 1 ? Invoice(code, "processing") : Invoice(200, "accepted", "KSEF-123"));
        var body = Body(await Send());
        Assert.True(body.Success);
        Assert.Equal(2, polls);
        Assert.Equal("KSEF-123", JsonSerializer.SerializeToElement(body.Data).GetProperty("ksefNumber").GetString());
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_Status429_PreservesRetryAfter()
    {
        Status(() => throw new KsefRateLimitException("limited", 42, null, null!));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(async _ => { await Send(); }, NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(context);
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("42", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, polls);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_StatusCircuitBreaker_PreservesRetryAfter()
    {
        Status(() => throw new KsefCircuitBreakerOpenException("open", TimeSpan.FromSeconds(17)));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(async _ => { await Send(); }, NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("17", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, polls);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_FailureAfterOpen_ClosesSession()
    {
        client.Setup(x => x.SendOnlineSessionInvoiceAsync(It.IsAny<SendInvoiceRequest>(), "session", "token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("send failed"));
        Assert.Equal("send failed", (await Assert.ThrowsAsync<InvalidOperationException>(Send)).Message);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_PollException_ClosesSessionWithoutMaskingPrimaryError()
    {
        var error = new KsefApiException("upstream", System.Net.HttpStatusCode.ServiceUnavailable, null!, null!);
        Status(() => throw error);
        closeError = new InvalidOperationException("close failed");
        Assert.Same(error, await Assert.ThrowsAsync<KsefApiException>(Send));
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_Cancellation_PropagatesAndClosesSession()
    {
        Status(() => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Send);
        Assert.Equal(1, polls);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_OpenFails_DoesNotCloseSession()
    {
        client.Setup(x => x.OpenOnlineSessionAsync(It.IsAny<OpenOnlineSessionRequest>(), "token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("open failed"));
        Assert.Equal("open failed", (await Assert.ThrowsAsync<InvalidOperationException>(Send)).Message);
        Assert.Equal(0, closes);
    }

    [Theory]
    [InlineData(405)] [InlineData(410)] [InlineData(415)] [InlineData(430)] [InlineData(435)]
    [InlineData(440)] [InlineData(450)] [InlineData(500)] [InlineData(550)] [InlineData(999)]
    public async Task Send_Rejected_ContainsCodeAndDescription(int code)
    {
        Status(() => Invoice(code, "rejected"));
        var result = await Send();
        Assert.Equal(502, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.False(Body(result).Success);
        Assert.Contains(code.ToString(), Body(result).Error);
        Assert.Contains("rejected", Body(result).Error);
        Assert.Equal(1, polls);
        Assert.Equal(1, closes);
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public async Task Send_Timeout_IsBoundedAndDistinctFromRejection(int code)
    {
        Status(() => Invoice(code, "processing"));
        var result = await Send();
        Assert.Equal(502, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Contains("timeout", Body(result).Error, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(clock.Elapsed.TotalSeconds, 60, 60);
        Assert.InRange(polls, 1, 30);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_PassesCancellationAndPublicKeyIdToSdk()
    {
        Assert.True(Body(await Send()).Success);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(cancellation.Token, tokens[0]);
        Assert.Equal(cancellation.Token, tokens[1]);
        Assert.True(tokens[2].CanBeCanceled);
        Assert.Equal(cancellation.Token, tokens[3]);
        Assert.Equal(new string('A', 43) + "=", opened!.Encryption.PublicKeyId);
    }

    [Fact]
    public async Task Send_SlowStatus_TimeCountsTowardTotalTimeout()
    {
        Status(() => { clock.Advance(TimeSpan.FromSeconds(19)); return Invoice(150); });
        var result = await Send();
        Assert.Contains("timeout", Body(result).Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, polls);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_CloseFails_DoesNotMaskAcceptedInvoice()
    {
        closeError = new InvalidOperationException("close failed");
        Assert.True(Body(await Send()).Success);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Send_NullStatus_WaitsUntilTimeout()
    {
        Status(() => null!);
        Assert.Contains("timeout", Body(await Send()).Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TimeSpan.FromSeconds(60), clock.Elapsed);
    }

    private sealed class Clock : TimeProvider
    {
        public TimeSpan Elapsed { get; private set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Elapsed.Ticks;
        public void Advance(TimeSpan duration) => Elapsed += duration;
    }
}
