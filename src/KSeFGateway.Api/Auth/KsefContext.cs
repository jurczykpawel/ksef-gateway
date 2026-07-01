namespace KSeFGateway.Api.Auth;

/// <summary>
/// Represents a single KSeF authentication context (one firm/NIP).
/// Authenticates either via a KSeF token, or via a KSeF certificate
/// (X.509 cert + private key, PEM files) using XAdES-signed auth requests.
/// </summary>
public record KsefContext
{
    public required string Nip { get; init; }
    public string? Token { get; init; }
    public string? CertificatePath { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? PrivateKeyPassword { get; init; }  // Set if the private key PEM is password-encrypted
    public string? Label { get; init; }  // Optional friendly name

    public bool UsesCertificate => !string.IsNullOrEmpty(CertificatePath);

    /// <summary>
    /// Throws if the context has neither a token nor a complete cert+key pair.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(Token) && string.IsNullOrEmpty(CertificatePath))
            throw new InvalidOperationException(
                $"KSeF context for NIP {Nip} needs either 'token' or 'certificatePath' + 'privateKeyPath'.");

        if (!string.IsNullOrEmpty(CertificatePath) && string.IsNullOrEmpty(PrivateKeyPath))
            throw new InvalidOperationException(
                $"KSeF context for NIP {Nip} has 'certificatePath' but no 'privateKeyPath' - both are required.");
    }
}
