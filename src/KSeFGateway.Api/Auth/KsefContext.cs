namespace KSeFGateway.Api.Auth;

/// <summary>
/// Represents a single KSeF authentication context (one firm/NIP).
/// Authenticates via exactly one of: a KSeF token, a certificate + key on disk (PEM files),
/// or a certificate + key supplied as PEM content (for platforms without file mounts) -
/// all using XAdES-signed auth requests when a certificate is used.
/// </summary>
public record KsefContext
{
    public required string Nip { get; init; }
    public string? Token { get; init; }
    public string? CertificatePath { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? CertificateContent { get; init; }
    public string? PrivateKeyContent { get; init; }
    public string? PrivateKeyPassword { get; init; }  // Set if the private key PEM is password-encrypted (path or content form)
    public string? Label { get; init; }  // Optional friendly name

    public bool HasCertificatePath => !string.IsNullOrEmpty(CertificatePath);
    public bool HasCertificateContent => !string.IsNullOrEmpty(CertificateContent);
    public bool UsesCertificate => HasCertificatePath || HasCertificateContent;

    /// <summary>
    /// Throws unless the context has exactly one auth method configured
    /// (token, certificate-on-disk, or certificate-as-content), with its key counterpart present.
    /// </summary>
    public void Validate()
    {
        var hasToken = !string.IsNullOrEmpty(Token);
        var methodCount = (hasToken ? 1 : 0) + (HasCertificatePath ? 1 : 0) + (HasCertificateContent ? 1 : 0);

        if (methodCount == 0)
            throw new InvalidOperationException(
                $"KSeF context for NIP {Nip} needs a 'token', 'certificatePath' + 'privateKeyPath', or 'certificateContent' + 'privateKeyContent'.");

        if (methodCount > 1)
            throw new InvalidOperationException(
                $"KSeF context for NIP {Nip} has more than one auth method configured - use exactly one of token / certificatePath / certificateContent.");

        if (HasCertificatePath && string.IsNullOrEmpty(PrivateKeyPath))
            throw new InvalidOperationException(
                $"KSeF context for NIP {Nip} has 'certificatePath' but no 'privateKeyPath' - both are required.");

        if (HasCertificateContent && string.IsNullOrEmpty(PrivateKeyContent))
            throw new InvalidOperationException(
                $"KSeF context for NIP {Nip} has 'certificateContent' but no 'privateKeyContent' - both are required.");
    }
}
