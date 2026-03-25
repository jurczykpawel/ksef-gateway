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
        if (httpContext.Request.Headers.TryGetValue(NipHeader, out var headerNip)
            && !string.IsNullOrEmpty(headerNip))
        {
            return headerNip.ToString();
        }

        // 2. Extract seller NIP from body (XML or JSON)
        if (bodyContent != null)
        {
            var nipFromBody = ExtractSellerNip(bodyContent);
            if (nipFromBody != null)
                return nipFromBody;
        }

        // 3. Default context
        return provider.GetDefault()?.Nip;
    }

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
