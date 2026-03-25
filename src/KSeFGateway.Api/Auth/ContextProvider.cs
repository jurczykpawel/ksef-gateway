using System.Text.Json;

namespace KSeFGateway.Api.Auth;

/// <summary>
/// Loads KSeF contexts from environment variables (single NIP)
/// or from contexts.json file (multi-NIP).
/// </summary>
public class ContextProvider
{
    private readonly IReadOnlyList<KsefContext> _contexts;
    private readonly string? _defaultNip;

    public ContextProvider(IConfiguration config, ILogger<ContextProvider> logger)
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
    }

    public IReadOnlyList<KsefContext> GetAll() => _contexts;

    public KsefContext? GetByNip(string nip) =>
        _contexts.FirstOrDefault(c => c.Nip == nip);

    public KsefContext? GetDefault() =>
        _defaultNip != null ? GetByNip(_defaultNip) : _contexts.FirstOrDefault();

    public bool IsMultiNip => _contexts.Count > 1;
}
