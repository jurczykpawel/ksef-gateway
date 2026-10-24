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
    // Send and buffer inside the same deadline; ownership never escapes this helper.
    public static async Task<(System.Net.HttpStatusCode Status, byte[] Body)> RequestAsync(
        IHttpClientFactory factory, IConfiguration config, string path, string body, string contentType,
        CancellationToken ct)
    {
        var seconds = config.GetValue<int?>("PDF_SERVICE_TIMEOUT_SECONDS") ?? 60;
        if (seconds <= 0) throw new InvalidOperationException("PDF_SERVICE_TIMEOUT_SECONDS must be positive");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        using var client = factory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl(config) + path);
        Authorize(request, config);
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, contentType);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(timeout.Token));
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("PDF service timeout", ex);
        }
    }

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
