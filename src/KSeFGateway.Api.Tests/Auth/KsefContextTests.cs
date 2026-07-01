using KSeFGateway.Api.Auth;

namespace KSeFGateway.Api.Tests.Auth;

public class KsefContextTests
{
    [Fact]
    public void UsesCertificate_WithCertificatePath_ReturnsTrue()
    {
        var context = new KsefContext { Nip = "1234567890", CertificatePath = "cert.crt", PrivateKeyPath = "cert.key" };
        Assert.True(context.UsesCertificate);
    }

    [Fact]
    public void UsesCertificate_WithTokenOnly_ReturnsFalse()
    {
        var context = new KsefContext { Nip = "1234567890", Token = "some-token" };
        Assert.False(context.UsesCertificate);
    }

    [Fact]
    public void Validate_WithTokenOnly_DoesNotThrow()
    {
        var context = new KsefContext { Nip = "1234567890", Token = "some-token" };
        context.Validate();
    }

    [Fact]
    public void Validate_WithCertificateAndKey_DoesNotThrow()
    {
        var context = new KsefContext { Nip = "1234567890", CertificatePath = "cert.crt", PrivateKeyPath = "cert.key" };
        context.Validate();
    }

    [Fact]
    public void Validate_WithNeitherTokenNorCertificate_Throws()
    {
        var context = new KsefContext { Nip = "1234567890" };
        var ex = Assert.Throws<InvalidOperationException>(context.Validate);
        Assert.Contains("1234567890", ex.Message);
    }

    [Fact]
    public void Validate_WithCertificatePathButNoPrivateKeyPath_Throws()
    {
        var context = new KsefContext { Nip = "1234567890", CertificatePath = "cert.crt" };
        var ex = Assert.Throws<InvalidOperationException>(context.Validate);
        Assert.Contains("privateKeyPath", ex.Message);
    }
}
