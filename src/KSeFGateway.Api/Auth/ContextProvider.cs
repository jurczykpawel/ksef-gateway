using System.Text.Json;
using KSeFGateway.Api.Licensing;

namespace KSeFGateway.Api.Auth;

/// <summary>
/// Loads KSeF contexts from environment variables (single NIP)
/// or from contexts.json file (multi-NIP). Multi-NIP requires a valid GATEWAY_LICENSE -
/// see LicenseService. Free tier gets the first configured NIP only.
/// </summary>
public class ContextProvider
{
    private IReadOnlyList<KsefContext> _contexts;
    private readonly string? _defaultNip;

    public ContextProvider(IConfiguration config, ILogger<ContextProvider> logger, LicenseService licenseService)
    {
        var contextsPath = config["KSEF_CONTEXTS_FILE"] ?? "/app/contexts.json";
        var envToken = config["KSEF_TOKEN"];
        var envNip = config["KSEF_NIP"];

        if (File.Exists(contextsPath))
        {
            var json = File.ReadAllText(contextsPath);
            var contexts = JsonSerializer.Deserialize<List<KsefContext>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            // Also add env var context if present and not already in file
            if (!string.IsNullOrEmpty(envToken) && !string.IsNullOrEmpty(envNip)
                && !contexts.Any(c => c.Nip == envNip))
            {
                contexts.Insert(0, new KsefContext { Nip = envNip, Token = envToken, Label = "env" });
            }

            _contexts = contexts;
            _defaultNip = envNip ?? contexts.FirstOrDefault()?.Nip;
            logger.LogInformation("Loaded {Count} KSeF contexts from {Path}. Default NIP: {Nip}",
                _contexts.Count, contextsPath, _defaultNip);
        }
        else if (!string.IsNullOrEmpty(envToken) && !string.IsNullOrEmpty(envNip))
        {
            _contexts = [new KsefContext { Nip = envNip, Token = envToken, Label = "env" }];
            _defaultNip = envNip;
            logger.LogInformation("Single KSeF context from env vars. NIP: {Nip}", envNip);
        }
        else
        {
            _contexts = [];
            _defaultNip = null;
            logger.LogWarning("No KSeF contexts configured. Set KSEF_TOKEN+KSEF_NIP or mount contexts.json");
        }

        var maxNips = licenseService.MaxNips;
        if (_contexts.Count > maxNips)
        {
            logger.LogWarning(
                "{Configured} NIPs configured but only {Max} allowed under your license - only {Max} will be active. " +
                "Get a license at https://sellf.techskills.academy/p/{Slug} to unlock the rest.",
                _contexts.Count, maxNips, maxNips, LicenseService.ProductSlug);

            // Keep the default NIP even if it wasn't first in the file/list - never silently
            // drop the context callers are actually relying on when truncating to the limit.
            var defaultContext = _defaultNip is not null ? _contexts.FirstOrDefault(c => c.Nip == _defaultNip) : null;
            var ordered = defaultContext is not null
                ? new[] { defaultContext }.Concat(_contexts.Where(c => c.Nip != _defaultNip))
                : _contexts;
            _contexts = ordered.Take(maxNips).ToList();
        }
    }

    public IReadOnlyList<KsefContext> GetAll() => _contexts;

    public KsefContext? GetByNip(string nip) =>
        _contexts.FirstOrDefault(c => c.Nip == nip);

    public KsefContext? GetDefault() =>
        _defaultNip != null ? GetByNip(_defaultNip) : _contexts.FirstOrDefault();

    public bool IsMultiNip => _contexts.Count > 1;
}
