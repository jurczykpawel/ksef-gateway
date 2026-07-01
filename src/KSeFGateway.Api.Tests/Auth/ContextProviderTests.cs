using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Tests.Licensing;

namespace KSeFGateway.Api.Tests.Auth;

public class ContextProviderTests
{
    private static string WriteContextsFile(int nipCount)
    {
        var contexts = Enumerable.Range(1, nipCount)
            .Select(i => new { nip = $"100000000{i}", token = $"token-{i}", label = $"context-{i}" });
        var path = Path.Combine(Path.GetTempPath(), $"contexts-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(contexts));
        return path;
    }

    private static IConfiguration MakeConfig(string contextsPath) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["KSEF_CONTEXTS_FILE"] = contextsPath })
        .Build();

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string NonExistentContextsPath() =>
        Path.Combine(Path.GetTempPath(), $"ksef-contexts-missing-{Guid.NewGuid():N}.json");

    private static async Task<LicenseService> MakeUnlicensedService()
    {
        var config = new ConfigurationBuilder().Build();
        var jwksClient = new JwksClient(new HttpClient(), LicenseService.DefaultJwksUrl, NullLogger<JwksClient>.Instance);
        var revocationClient = new RevocationClient(new HttpClient(), LicenseService.DefaultRevocationUrl, NullLogger<RevocationClient>.Instance);
        var service = new LicenseService(jwksClient, revocationClient, config, NullLogger<LicenseService>.Instance);
        await service.RefreshAsync(); // no GATEWAY_LICENSE configured -> free tier, no network call made
        return service;
    }

    private static async Task<LicenseService> MakeLicensedService()
    {
        const string kid = "test-kid";
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        var claims = new
        {
            v = 1,
            kid,
            product = LicenseService.ProductSlug,
            email = "buyer@example.com",
            order = "order-1",
            tier = "unlimited",
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = (long?)null,
        };
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
        var sig = ecdsa.SignData(Encoding.UTF8.GetBytes(payloadB64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var token = $"{payloadB64}.{Base64UrlEncode(sig)}";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GATEWAY_LICENSE"] = token })
            .Build();

        var jwksHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { keys = new[] { new { kid, alg = "ES256", pem = publicKeyPem } } })),
        });
        var revocationHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"order_hashes":[]}"""),
        });

        var jwksClient = new JwksClient(new HttpClient(jwksHandler), LicenseService.DefaultJwksUrl, NullLogger<JwksClient>.Instance);
        var revocationClient = new RevocationClient(new HttpClient(revocationHandler), LicenseService.DefaultRevocationUrl, NullLogger<RevocationClient>.Instance);
        var service = new LicenseService(jwksClient, revocationClient, config, NullLogger<LicenseService>.Instance);
        await service.RefreshAsync();
        return service;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public async Task FreeTier_SingleNipConfigured_AllActive()
    {
        var path = WriteContextsFile(1);
        var licenseService = await MakeUnlicensedService();

        var provider = new ContextProvider(MakeConfig(path), NullLogger<ContextProvider>.Instance, licenseService);

        Assert.Single(provider.GetAll());
    }

    [Fact]
    public async Task FreeTier_MultipleNipsConfigured_OnlyFirstIsActive()
    {
        var path = WriteContextsFile(3);
        var licenseService = await MakeUnlicensedService();

        var provider = new ContextProvider(MakeConfig(path), NullLogger<ContextProvider>.Instance, licenseService);

        var all = provider.GetAll();
        Assert.Single(all);
        Assert.Equal("1000000001", all[0].Nip);
    }

    [Fact]
    public async Task FreeTier_MultipleNipsConfigured_ActivatesInDeterministicOrder()
    {
        var path = WriteContextsFile(5);
        var licenseService = await MakeUnlicensedService();

        var provider = new ContextProvider(MakeConfig(path), NullLogger<ContextProvider>.Instance, licenseService);

        Assert.Equal("1000000001", provider.GetAll()[0].Nip);
    }

    [Fact]
    public async Task Licensed_MultipleNipsConfigured_AllActive()
    {
        var path = WriteContextsFile(5);
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(MakeConfig(path), NullLogger<ContextProvider>.Instance, licenseService);

        Assert.Equal(5, provider.GetAll().Count);
    }

    [Fact]
    public async Task EnvVars_TokenOnly_LoadsSingleTokenContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_TOKEN"] = "some-token",
            ["KSEF_NIP"] = "1234567890",
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.Equal("1234567890", context.Nip);
        Assert.Equal("some-token", context.Token);
        Assert.False(context.UsesCertificate);
    }

    [Fact]
    public async Task EnvVars_CertAndKeyOnly_LoadsSingleCertificateContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.Equal("1234567890", context.Nip);
        Assert.True(context.UsesCertificate);
        Assert.Equal("/app/certs/company.crt", context.CertificatePath);
        Assert.Equal("/app/certs/company.key", context.PrivateKeyPath);
    }

    [Fact]
    public async Task EnvVars_TokenAndCertContentBothSet_UsesTokenAndWarns()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_TOKEN"] = "some-token",
            ["KSEF_CERT_CONTENT"] = "-----BEGIN CERTIFICATE-----\nfake\n-----END CERTIFICATE-----",
            ["KSEF_KEY_CONTENT"] = "-----BEGIN PRIVATE KEY-----\nfake\n-----END PRIVATE KEY-----",
        });
        var licenseService = await MakeLicensedService();
        var logger = new CapturingLogger<ContextProvider>();

        var provider = new ContextProvider(config, logger, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.False(context.UsesCertificate);
        Assert.Equal("some-token", context.Token);
        Assert.Contains(logger.Warnings, w => w.Contains("Multiple KSeF auth methods configured"));
    }

    [Fact]
    public async Task EnvVars_CertKeyAndPassword_LoadsPasswordOnContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
            ["KSEF_KEY_PASSWORD"] = "s3cret!",
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.Equal("s3cret!", context.PrivateKeyPassword);
    }

    [Fact]
    public async Task EnvVars_CertContentAndKeyContentOnly_LoadsContentBasedContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_CONTENT"] = "-----BEGIN CERTIFICATE-----\nMII...\n-----END CERTIFICATE-----",
            ["KSEF_KEY_CONTENT"] = "-----BEGIN PRIVATE KEY-----\nMII...\n-----END PRIVATE KEY-----",
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.True(context.UsesCertificate);
        Assert.False(context.HasCertificatePath);
        Assert.True(context.HasCertificateContent);
    }

    [Fact]
    public async Task EnvVars_CertPathTakesPriorityOverCertContent_WhenBothPresent()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
            ["KSEF_CERT_CONTENT"] = "-----BEGIN CERTIFICATE-----",
            ["KSEF_KEY_CONTENT"] = "-----BEGIN PRIVATE KEY-----",
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.True(context.HasCertificatePath);
        Assert.False(context.HasCertificateContent);
    }

    [Fact]
    public async Task EnvVars_TokenTakesPriorityOverCert_WhenBothPresent()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_TOKEN"] = "some-token",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        var context = Assert.Single(provider.GetAll());
        Assert.False(context.UsesCertificate);
    }

    [Fact]
    public async Task NoEnvVarsNoFile_LoadsEmptyContextList()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
        });
        var licenseService = await MakeLicensedService();

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

        Assert.Empty(provider.GetAll());
        Assert.Null(provider.GetDefault());
    }

    [Fact]
    public async Task ContextsFile_WithMixedTokenAndCertificateEntries_LoadsBoth()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ksef-contexts-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        [
          { "nip": "1111111111", "token": "token-a", "label": "Company A" },
          { "nip": "2222222222", "certificatePath": "/app/certs/b.crt", "privateKeyPath": "/app/certs/b.key", "label": "Company B" }
        ]
        """);

        try
        {
            var config = BuildConfig(new() { ["KSEF_CONTEXTS_FILE"] = path });
            var licenseService = await MakeLicensedService();
            var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService);

            Assert.Equal(2, provider.GetAll().Count);
            Assert.False(provider.GetByNip("1111111111")!.UsesCertificate);
            Assert.True(provider.GetByNip("2222222222")!.UsesCertificate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ContextsFile_WithCertificatePathButNoKeyPath_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ksef-contexts-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        [
          { "nip": "1111111111", "certificatePath": "/app/certs/a.crt" }
        ]
        """);

        try
        {
            var config = BuildConfig(new() { ["KSEF_CONTEXTS_FILE"] = path });
            var licenseService = await MakeLicensedService();
            Assert.Throws<InvalidOperationException>(() =>
                new ContextProvider(config, NullLogger<ContextProvider>.Instance, licenseService));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
