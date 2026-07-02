namespace KSeFGateway.Api.Endpoints;

/// <summary>
/// Helpers for talking to the companion PDF service (ksef-pdf). Centralizes two things so every
/// call site behaves identically:
/// <list type="number">
/// <item>Normalizing <c>PDF_SERVICE_URL</c> to include a scheme. Render's Blueprint
/// <c>fromService … property: hostport</c> yields a bare <c>host:port</c> with no scheme, which
/// <see cref="HttpClient"/> rejects ("scheme is not supported"). We default the scheme to http.</item>
/// <item>Attaching the optional shared secret <c>PDF_SERVICE_SECRET</c>. Render free web services
/// cannot receive private-network traffic, so there the PDF service must be reached over its public
/// HTTPS URL - the secret lets it stay closed instead of being an open XML→PDF endpoint. On a
/// private network (local compose, paid Render private networking) leave the secret unset.</item>
/// </list>
/// </summary>
public static class PdfService
{
    public const string SecretHeader = "X-Pdf-Secret";

    /// <summary>Reads <c>PDF_SERVICE_URL</c>, ensures it has an http/https scheme, trims a trailing slash.</summary>
    public static string BaseUrl(IConfiguration config)
    {
        var raw = (config["PDF_SERVICE_URL"] ?? "http://ksef-pdf:3000").Trim();
        if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            raw = "http://" + raw;
        return raw.TrimEnd('/');
    }

    /// <summary>Adds the <see cref="SecretHeader"/> when <c>PDF_SERVICE_SECRET</c> is configured; no-op otherwise.</summary>
    public static void Authorize(HttpRequestMessage request, IConfiguration config)
    {
        var secret = config["PDF_SERVICE_SECRET"];
        if (!string.IsNullOrWhiteSpace(secret))
            request.Headers.Add(SecretHeader, secret);
    }
}
