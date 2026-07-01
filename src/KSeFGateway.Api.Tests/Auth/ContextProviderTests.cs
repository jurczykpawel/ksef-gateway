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
}
