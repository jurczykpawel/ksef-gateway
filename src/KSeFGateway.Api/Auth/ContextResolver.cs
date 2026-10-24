using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace KSeFGateway.Api.Auth;

/// <summary>
/// Resolves which KSeF context (NIP) to use for a request.
/// Priority: X-KSeF-NIP header > seller NIP from body > default context.
/// </summary>
public static class ContextResolver
{
    public const string NipHeader = "X-KSeF-NIP";

    /// <summary>
    /// Resolves NIP from request. Returns null if no context can be determined.
    /// </summary>
    public static string? ResolveNip(HttpContext httpContext, ContextProvider provider, string? bodyContent = null)
    {
        // A header must not hide an unauthorized seller in the payload.
        if (ApiKeyAccess.AllowedNips(httpContext) != null && bodyContent != null &&
            ExtractSellerNip(bodyContent) is { } sellerNip)
            ValidateNip(sellerNip, provider, httpContext);

        // 1. Explicit header (highest priority)
        var headerNip = ValidateHeader(httpContext, provider);
        if (headerNip != null)
            return headerNip;

        if (ApiKeyAccess.AllowedNips(httpContext) is { Count: > 1 })
            throw new BadHttpRequestException("X-KSeF-NIP is required for an API key authorized for multiple NIPs.", 400);

        // 2. Extract seller NIP from body (XML or JSON)
        if (bodyContent != null)
        {
            var nipFromBody = ExtractSellerNip(bodyContent);
            if (nipFromBody != null)
                return ValidateNip(nipFromBody, provider, httpContext);
        }

        // 3. Default context
        var defaultNip = ApiKeyAccess.DefaultNip(httpContext, provider);
        return defaultNip != null ? ValidateNip(defaultNip, provider, httpContext) : null;
    }

    /// <summary>Validates an explicit header before rate limiting or endpoint execution.</summary>
    public static string? ValidateHeader(HttpContext httpContext, ContextProvider provider)
    {
        if (!httpContext.Request.Headers.TryGetValue(NipHeader, out var values))
            return null;

        if (values.Count != 1)
            throw InvalidNip();

        return ValidateNip(values[0], provider, httpContext);
    }

    /// <summary>Rejects untrusted NIPs before any per-NIP state is allocated.</summary>
    public static string ValidateNip(string? nip, ContextProvider provider, HttpContext? httpContext = null)
    {
        // Length first: bounded work and no copies/logging of arbitrary header values.
        if (nip is null || nip.Length != 10 || nip.Any(c => c < '0' || c > '9'))
            throw InvalidNip();

        if (httpContext != null)
            ApiKeyAccess.Authorize(httpContext, nip);

        if (provider.GetByNip(nip) is null)
            throw new BadHttpRequestException(
                $"No context configured for NIP {nip}" + (provider.IsLicenseRestricted(nip)
                    ? "; a valid Multi-NIP license is required to access this context" : ""),
                StatusCodes.Status404NotFound);

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
        if (body.TrimStart().StartsWith('<'))
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                var root = XElement.Load(reader);
                return root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Podmiot1")?
                    .Descendants().FirstOrDefault(e => e.Name.LocalName == "NIP")?.Value;
            }
            catch (XmlException) { return null; }
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var friendly = Property(Property(root, "seller"), "nip");
            if (friendly.ValueKind == JsonValueKind.String)
                return friendly.GetString();

            var invoice = Property(root, "Faktura");
            if (invoice.ValueKind != JsonValueKind.Object) invoice = root;
            var seller = Property(invoice, "Podmiot1");
            var identity = Property(seller, "DaneIdentyfikacyjne");
            if (identity.ValueKind != JsonValueKind.Object) identity = seller;
            var nip = Property(Property(identity, "NIP"), "_text");
            return nip.ValueKind == JsonValueKind.String ? nip.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().LastOrDefault(p => string.Equals(p.Name.Split(':')[^1], name, StringComparison.OrdinalIgnoreCase)).Value
            : default;
}
