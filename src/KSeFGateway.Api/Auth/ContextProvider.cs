using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using KSeFGateway.Api.Middleware;
using KSeFGateway.Api.Licensing;

namespace KSeFGateway.Api.Auth;

/// <summary>
/// Loads KSeF contexts from environment variables (single NIP)
/// or from contexts.json file (multi-NIP). Multi-NIP requires a valid GATEWAY_LICENSE -
/// see LicenseService. All contexts are retained; free tier can use only the default NIP.
/// </summary>
public class ContextProvider
{
    private readonly IReadOnlyList<KsefContext> _contexts;
    private readonly LicenseService _licenseService;
    private readonly string? _defaultNip;

    public ContextProvider(IConfiguration config, ILogger<ContextProvider> logger, LicenseService licenseService)
    {
        _licenseService = licenseService;
        var contextsPath = config["KSEF_CONTEXTS_FILE"] ?? "/app/contexts.json";
        var envToken = config["KSEF_TOKEN"];
        var envNip = config["KSEF_NIP"];
        var envCertPath = config["KSEF_CERT_PATH"];
        var envKeyPath = config["KSEF_KEY_PATH"];
        var envKeyPassword = config["KSEF_KEY_PASSWORD"];
        var envCertContent = config["KSEF_CERT_CONTENT"];
        var envKeyContent = config["KSEF_KEY_CONTENT"];

        var configuredAuthMethods = new List<string>();
        if (!string.IsNullOrEmpty(envToken)) configuredAuthMethods.Add("KSEF_TOKEN");
        if (!string.IsNullOrEmpty(envCertPath) && !string.IsNullOrEmpty(envKeyPath)) configuredAuthMethods.Add("KSEF_CERT_PATH+KSEF_KEY_PATH");
        if (!string.IsNullOrEmpty(envCertContent) && !string.IsNullOrEmpty(envKeyContent)) configuredAuthMethods.Add("KSEF_CERT_CONTENT+KSEF_KEY_CONTENT");
        if (configuredAuthMethods.Count > 1)
            logger.LogWarning(
                "Multiple KSeF auth methods configured for env NIP {Nip}: {Methods} - using {Winner}, silently " +
                "ignoring the rest. Remove the ones you're not using.",
                envNip, string.Join(", ", configuredAuthMethods), configuredAuthMethods[0]);

        var envContext = BuildEnvContext(envNip, envToken, envCertPath, envKeyPath, envKeyPassword, envCertContent, envKeyContent);

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
            _defaultNip = contexts.Any(c => c.Nip == envNip) ? envNip : contexts.FirstOrDefault()?.Nip;
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
            logger.LogWarning("No KSeF contexts configured. Set KSEF_TOKEN+KSEF_NIP, KSEF_CERT_PATH+KSEF_KEY_PATH+KSEF_NIP, KSEF_CERT_CONTENT+KSEF_KEY_CONTENT+KSEF_NIP, or mount contexts.json");
        }

        if (_contexts.Count > licenseService.MaxNips)
            logger.LogWarning("Multi-NIP license required: only default NIP {Nip} is available; additional contexts are retained for license recovery", GetDefault()?.Nip);
    }

    private static KsefContext? BuildEnvContext(
        string? nip, string? token, string? certPath, string? keyPath, string? keyPassword,
        string? certContent, string? keyContent)
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
        if (!string.IsNullOrEmpty(certContent) && !string.IsNullOrEmpty(keyContent))
            return new KsefContext
            {
                Nip = nip,
                CertificateContent = certContent,
                PrivateKeyContent = keyContent,
                PrivateKeyPassword = keyPassword,
                Label = "env"
            };
        return null;
    }

    public IReadOnlySet<string> FindNipsForApiKey(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var nips = new HashSet<string>(StringComparer.Ordinal);
        // Inspect every configured hash; compare fixed-length digests through the shared constant-time helper.
        foreach (var context in GetAll())
            foreach (var configured in context.ApiKeys)
                if (SecretComparison.ConstantTimeEquals(hash, configured[7..].ToUpperInvariant()))
                    nips.Add(context.Nip);
        return nips;
    }

    public IReadOnlyList<KsefContext> GetAll() =>
        _licenseService.MaxNips > 1 ? _contexts : _contexts.Where(c => c.Nip == _defaultNip).ToArray();

    public KsefContext? GetByNip(string nip) =>
        nip == _defaultNip || _licenseService.MaxNips > 1
            ? _contexts.FirstOrDefault(c => c.Nip == nip)
            : null;

    public KsefContext? GetDefault() =>
        _contexts.FirstOrDefault(c => c.Nip == _defaultNip);

    public bool IsMultiNip => GetAll().Count > 1;

    public bool IsLicenseRestricted(string nip) =>
        _licenseService.MaxNips <= 1 && nip != _defaultNip && _contexts.Any(c => c.Nip == nip);
}
