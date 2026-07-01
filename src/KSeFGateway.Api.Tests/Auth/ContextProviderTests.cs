using KSeFGateway.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Auth;

public class ContextProviderTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string NonExistentContextsPath() =>
        Path.Combine(Path.GetTempPath(), $"ksef-contexts-missing-{Guid.NewGuid():N}.json");

    [Fact]
    public void EnvVars_TokenOnly_LoadsSingleTokenContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_TOKEN"] = "some-token",
            ["KSEF_NIP"] = "1234567890",
        });

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        var context = Assert.Single(provider.GetAll());
        Assert.Equal("1234567890", context.Nip);
        Assert.Equal("some-token", context.Token);
        Assert.False(context.UsesCertificate);
    }

    [Fact]
    public void EnvVars_CertAndKeyOnly_LoadsSingleCertificateContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
        });

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        var context = Assert.Single(provider.GetAll());
        Assert.Equal("1234567890", context.Nip);
        Assert.True(context.UsesCertificate);
        Assert.Equal("/app/certs/company.crt", context.CertificatePath);
        Assert.Equal("/app/certs/company.key", context.PrivateKeyPath);
    }

    [Fact]
    public void EnvVars_CertKeyAndPassword_LoadsPasswordOnContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
            ["KSEF_KEY_PASSWORD"] = "s3cret!",
        });

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        var context = Assert.Single(provider.GetAll());
        Assert.Equal("s3cret!", context.PrivateKeyPassword);
    }

    [Fact]
    public void EnvVars_CertContentAndKeyContentOnly_LoadsContentBasedContext()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_CERT_CONTENT"] = "-----BEGIN CERTIFICATE-----\nMII...\n-----END CERTIFICATE-----",
            ["KSEF_KEY_CONTENT"] = "-----BEGIN PRIVATE KEY-----\nMII...\n-----END PRIVATE KEY-----",
        });

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        var context = Assert.Single(provider.GetAll());
        Assert.True(context.UsesCertificate);
        Assert.False(context.HasCertificatePath);
        Assert.True(context.HasCertificateContent);
    }

    [Fact]
    public void EnvVars_CertPathTakesPriorityOverCertContent_WhenBothPresent()
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

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        var context = Assert.Single(provider.GetAll());
        Assert.True(context.HasCertificatePath);
        Assert.False(context.HasCertificateContent);
    }

    [Fact]
    public void EnvVars_TokenTakesPriorityOverCert_WhenBothPresent()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
            ["KSEF_NIP"] = "1234567890",
            ["KSEF_TOKEN"] = "some-token",
            ["KSEF_CERT_PATH"] = "/app/certs/company.crt",
            ["KSEF_KEY_PATH"] = "/app/certs/company.key",
        });

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        var context = Assert.Single(provider.GetAll());
        Assert.False(context.UsesCertificate);
    }

    [Fact]
    public void NoEnvVarsNoFile_LoadsEmptyContextList()
    {
        var config = BuildConfig(new()
        {
            ["KSEF_CONTEXTS_FILE"] = NonExistentContextsPath(),
        });

        var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

        Assert.Empty(provider.GetAll());
        Assert.Null(provider.GetDefault());
    }

    [Fact]
    public void ContextsFile_WithMixedTokenAndCertificateEntries_LoadsBoth()
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
            var provider = new ContextProvider(config, NullLogger<ContextProvider>.Instance);

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
    public void ContextsFile_WithCertificatePathButNoKeyPath_Throws()
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
            Assert.Throws<InvalidOperationException>(() =>
                new ContextProvider(config, NullLogger<ContextProvider>.Instance));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
