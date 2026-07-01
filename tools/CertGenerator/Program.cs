using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Builders.X509Certificates;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.DI;
using Microsoft.Extensions.DependencyInjection;

const string BaseUrl = "https://api-test.ksef.mf.gov.pl";
const string OutputDir = "/app/output";
const int PollDelayMs = 2000;
const int MaxPollAttempts = 60;

// Generate random valid NIP for testing
string nip = GenerateRandomNip();

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║   KSeF Gateway — Test Certificate Generator ║");
Console.WriteLine("╠══════════════════════════════════════════════╣");
Console.WriteLine($"║  Environment: TEST                           ║");
Console.WriteLine($"║  NIP:         {nip}                    ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine();

// Setup DI
var services = new ServiceCollection();
services.AddKSeFClient(options => { options.BaseUrl = BaseUrl; });
services.AddCryptographyClient();
var provider = services.BuildServiceProvider();

var authClient = provider.GetRequiredService<IAuthorizationClient>();
var cryptoService = provider.GetRequiredService<ICryptographyService>();

Console.Write("Waiting for crypto warmup...");
await cryptoService.WarmupAsync();
Console.WriteLine(" done.");
Console.WriteLine();

// Step 1: Build a self-signed certificate (accepted on TEST only)
Console.WriteLine("[1/3] Creating self-signed certificate...");
X509Certificate2 certificate = SelfSignedCertificateForSignatureBuilder
    .Create()
    .WithGivenName("Test")
    .WithSurname("User")
    .WithSerialNumber($"TINPL-{nip}")
    .WithCommonName("Test User")
    .Build();

// Step 2: Prove it actually authenticates before handing it over
Console.WriteLine("[2/3] Verifying the certificate authenticates against KSeF TEST...");
var challenge = await authClient.GetAuthChallengeAsync();

var authTokenRequest = AuthTokenRequestBuilder
    .Create()
    .WithChallenge(challenge.Challenge)
    .WithContext(AuthenticationTokenContextIdentifierType.Nip, nip)
    .WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject)
    .Build();

string unsignedXml = AuthenticationTokenRequestSerializer.SerializeToXmlString(authTokenRequest);
string signedXml = SignatureService.Sign(unsignedXml, certificate);

var authOp = await authClient.SubmitXadesAuthRequestAsync(signedXml, verifyCertificateChain: false, enforceXadesCompliance: true);

Console.Write("  Waiting for auth to complete");
AuthStatus? authStatus = null;
for (int i = 0; i < MaxPollAttempts; i++)
{
    await Task.Delay(PollDelayMs);
    authStatus = await authClient.GetAuthStatusAsync(authOp.ReferenceNumber, authOp.AuthenticationToken.Token);
    Console.Write(".");
    if (authStatus.Status.Code != 100) break;
}
Console.WriteLine();

if (authStatus?.Status.Code != 200)
{
    Console.Error.WriteLine($"  VERIFICATION FAILED: code={authStatus?.Status.Code}, desc={authStatus?.Status.Description}");
    Environment.Exit(1);
}

Console.WriteLine("  Verified - the certificate authenticates successfully.");
Console.WriteLine();

// Step 3: Export certificate + private key as PEM
Console.WriteLine("[3/3] Exporting certificate + private key (PEM)...");
Directory.CreateDirectory(OutputDir);
string certPath = Path.Combine(OutputDir, "test-cert.crt");
string keyPath = Path.Combine(OutputDir, "test-cert.key");
string nipPath = Path.Combine(OutputDir, "test-cert.nip");

await File.WriteAllTextAsync(certPath, certificate.ExportCertificatePem());
await File.WriteAllTextAsync(keyPath, certificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());
await File.WriteAllTextAsync(nipPath, nip);

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  YOUR KSEF TEST CERTIFICATE                                  ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"  KSEF_CERT_PATH=./output/test-cert.crt");
Console.WriteLine($"  KSEF_KEY_PATH=./output/test-cert.key");
Console.WriteLine($"  KSEF_NIP={nip}");
Console.WriteLine($"  KSEF_ENV=TEST");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("Mount ./output into the gateway (or copy the files) and set the vars above,");
Console.WriteLine("or add them to contexts.json as certificatePath/privateKeyPath. See README:");
Console.WriteLine("\"Certificate-Based Auth\".");
Console.WriteLine();
Console.WriteLine("Self-signed certificates are accepted on TEST only - production requires");
Console.WriteLine("a real KSeF-issued certificate from the portal.");

// Random NIP generator with valid checksum (same as TokenGenerator)
static string GenerateRandomNip()
{
    int[] weights = { 6, 5, 7, 2, 3, 4, 5, 6, 7 };
    var rng = new Random();
    int[] digits = new int[10];

    do
    {
        for (int i = 0; i < 9; i++)
            digits[i] = rng.Next(0, 10);

        // Ensure first digit is not 0
        if (digits[0] == 0) digits[0] = rng.Next(1, 10);

        int sum = 0;
        for (int i = 0; i < 9; i++)
            sum += digits[i] * weights[i];

        digits[9] = sum % 11;
    } while (digits[9] == 10); // checksum digit must be 0-9

    return string.Join("", digits);
}
