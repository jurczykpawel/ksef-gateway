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
        var envCertPath = config["KSEF_CERT_PATH"];
        var envKeyPath = config["KSEF_KEY_PATH"];
        var envKeyPassword = config["KSEF_KEY_PASSWORD"];
        var envContext = BuildEnvContext(envNip, envToken, envCertPath, envKeyPath, envKeyPassword);

        if (File.Exists(contextsPath))
        {
            var json = File.ReadAllText(contextsPath);
            var contexts = JsonSerializer.Deserialize<List<KsefContext>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            foreach (var context in contexts)
                context.Validate();

            // Also add env var context if present and not already in file
            if (envContext != null && !contexts.Any(c => c.Nip == envContext.Nip))
                contexts.Insert(0, envContext);

            _contexts = contexts;
            _defaultNip = envNip ?? contexts.FirstOrDefault()?.Nip;
            logger.LogInformation("Loaded {Count} KSeF contexts from {Path}. Default NIP: {Nip}",
                _contexts.Count, contextsPath, _defaultNip);
        }
        else if (envContext != null)
        {
            _contexts = [envContext];
            _defaultNip = envNip;
            logger.LogInformation("Single KSeF context from env vars. NIP: {Nip} ({Method})",
                envNip, envContext.UsesCertificate ? "certificate" : "token");
        }
        else
        {
            _contexts = [];
            _defaultNip = null;
            logger.LogWarning("No KSeF contexts configured. Set KSEF_TOKEN+KSEF_NIP, KSEF_CERT_PATH+KSEF_KEY_PATH+KSEF_NIP, or mount contexts.json");
        }
    }

    private static KsefContext? BuildEnvContext(string? nip, string? token, string? certPath, string? keyPath, string? keyPassword)
    {
        if (string.IsNullOrEmpty(nip))
            return null;
        if (!string.IsNullOrEmpty(token))
            return new KsefContext { Nip = nip, Token = token, Label = "env" };
        if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(keyPath))
            return new KsefContext
            {
                Nip = nip,
                CertificatePath = certPath,
                PrivateKeyPath = keyPath,
                PrivateKeyPassword = keyPassword,
                Label = "env"
            };
        return null;
    }

    public IReadOnlyList<KsefContext> GetAll() => _contexts;

    public KsefContext? GetByNip(string nip) =>
        _contexts.FirstOrDefault(c => c.Nip == nip);

    public KsefContext? GetDefault() =>
        _defaultNip != null ? GetByNip(_defaultNip) : _contexts.FirstOrDefault();

    public bool IsMultiNip => _contexts.Count > 1;
}
