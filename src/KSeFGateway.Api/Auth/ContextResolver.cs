using System.Text.RegularExpressions;

namespace KSeFGateway.Api.Auth;

/// <summary>
/// Resolves which KSeF context (NIP) to use for a request.
/// Priority: X-KSeF-NIP header > seller NIP from body > default context.
/// </summary>
public static partial class ContextResolver
{
    public const string NipHeader = "X-KSeF-NIP";

    /// <summary>
    /// Resolves NIP from request. Returns null if no context can be determined.
    /// </summary>
    public static string? ResolveNip(HttpContext httpContext, ContextProvider provider, string? bodyContent = null)
    {
        // 1. Explicit header (highest priority)
        var headerNip = ValidateHeader(httpContext, provider);
        if (headerNip != null)
            return headerNip;

        // 2. Extract seller NIP from body (XML or JSON)
        if (bodyContent != null)
        {
            var nipFromBody = ExtractSellerNip(bodyContent);
            if (nipFromBody != null)
                return ValidateNip(nipFromBody, provider);
        }

        // 3. Default context
        var defaultNip = provider.GetDefault()?.Nip;
        return defaultNip != null ? ValidateNip(defaultNip, provider) : null;
    }

    /// <summary>Validates an explicit header before rate limiting or endpoint execution.</summary>
    public static string? ValidateHeader(HttpContext httpContext, ContextProvider provider)
    {
        if (!httpContext.Request.Headers.TryGetValue(NipHeader, out var values))
            return null;

        if (values.Count != 1)
            throw InvalidNip();

        return ValidateNip(values[0], provider);
    }

    /// <summary>Rejects untrusted NIPs before any per-NIP state is allocated.</summary>
    public static string ValidateNip(string? nip, ContextProvider provider)
    {
        // Length first: bounded work and no copies/logging of arbitrary header values.
        if (nip is null || nip.Length != 10 || nip.Any(c => c < '0' || c > '9'))
            throw InvalidNip();

        if (provider.GetByNip(nip) is null)
            throw new BadHttpRequestException($"No context configured for NIP {nip}", StatusCodes.Status404NotFound);

        return nip;
    }

    private static BadHttpRequestException InvalidNip() => new(
        "NIP must contain exactly 10 ASCII digits; X-KSeF-NIP must have a single value.",
        StatusCodes.Status400BadRequest);

    /// <summary>
    /// Extracts seller NIP from invoice XML or JSON body.
    /// </summary>
    public static string? ExtractSellerNip(string body)
    {
        // XML: <Podmiot1>...<NIP>1234567890</NIP>
        var xmlMatch = XmlNipRegex().Match(body);
        if (xmlMatch.Success)
            return xmlMatch.Groups[1].Value;

        // JSON (friendly): "seller": { "nip": "1234567890"
        var jsonMatch = JsonNipRegex().Match(body);
        if (jsonMatch.Success)
            return jsonMatch.Groups[1].Value;

        // JSON (xml-js): "NIP": { "_text": "1234567890"
        var xmlJsMatch = XmlJsNipRegex().Match(body);
        if (xmlJsMatch.Success)
            return xmlJsMatch.Groups[1].Value;

        return null;
    }

    [GeneratedRegex(@"<Podmiot1>.*?<NIP>(\d{10})</NIP>", RegexOptions.Singleline)]
    private static partial Regex XmlNipRegex();

    [GeneratedRegex(@"""seller"".*?""nip""\s*:\s*""(\d{10})""", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonNipRegex();

    [GeneratedRegex(@"""Podmiot1"".*?""NIP"".*?""_text""\s*:\s*""(\d{10})""", RegexOptions.Singleline)]
    private static partial Regex XmlJsNipRegex();
}
