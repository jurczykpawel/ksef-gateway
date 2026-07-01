using System.Security.Cryptography.X509Certificates;
using System.Text;
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
const int PollDelayMs = 2000;
const int MaxPollAttempts = 60;

// Generate random valid NIP for testing
string nip = GenerateRandomNip();

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║   KSeF Gateway — Test Token Generator       ║");
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
var ksefClient = provider.GetRequiredService<IKSeFClient>();
var cryptoService = provider.GetRequiredService<ICryptographyService>();

// Wait for crypto warmup
Console.Write("Waiting for crypto warmup...");
await cryptoService.WarmupAsync();
Console.WriteLine(" done.");
Console.WriteLine();

// Step 1: Authenticate with self-signed certificate (TEST env only)
Console.WriteLine("[1/5] Getting auth challenge...");
var challenge = await authClient.GetAuthChallengeAsync();
Console.WriteLine($"  Challenge: {challenge.Challenge[..20]}...");

Console.WriteLine("[2/5] Creating self-signed certificate and signing XAdES...");
var authTokenRequest = AuthTokenRequestBuilder
    .Create()
    .WithChallenge(challenge.Challenge)
    .WithContext(AuthenticationTokenContextIdentifierType.Nip, nip)
    .WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject)
    .Build();

string unsignedXml = AuthenticationTokenRequestSerializer.SerializeToXmlString(authTokenRequest);

X509Certificate2 certificate = SelfSignedCertificateForSignatureBuilder
    .Create()
    .WithGivenName("Test")
    .WithSurname("User")
    .WithSerialNumber($"TINPL-{nip}")
    .WithCommonName("Test User")
    .Build();

string signedXml = SignatureService.Sign(unsignedXml, certificate);
Console.WriteLine("  Signed.");

Console.WriteLine("[3/5] Submitting auth request...");
var authOp = await authClient.SubmitXadesAuthRequestAsync(signedXml, verifyCertificateChain: false, enforceXadesCompliance: true);
Console.WriteLine($"  Reference: {authOp.ReferenceNumber}");

// Poll for auth completion
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
    Console.Error.WriteLine($"  AUTH FAILED: code={authStatus?.Status.Code}, desc={authStatus?.Status.Description}");
    Environment.Exit(1);
}

Console.WriteLine("  Auth completed successfully.");

// Get access token
var tokens = await authClient.GetAccessTokenAsync(authOp.AuthenticationToken.Token);
string accessToken = tokens.AccessToken.Token;
Console.WriteLine($"  Access token obtained (expires: {tokens.AccessToken.ValidUntil})");
Console.WriteLine();

// Step 2: Generate KSeF token
Console.WriteLine("[4/5] Generating KSeF token with InvoiceRead + InvoiceWrite permissions...");
var tokenRequest = new KsefTokenRequest
{
    Permissions = [
        KsefTokenPermissionType.InvoiceRead,
        KsefTokenPermissionType.InvoiceWrite,
        KsefTokenPermissionType.CredentialsRead
    ],
    Description = "ksef-gateway test token"
};

var tokenResponse = await ksefClient.GenerateKsefTokenAsync(tokenRequest, accessToken);
Console.WriteLine($"  Token reference: {tokenResponse.ReferenceNumber}");

// Wait for token activation
Console.Write("  Waiting for token activation");
AuthenticationKsefToken? activeToken = null;
for (int i = 0; i < MaxPollAttempts; i++)
{
    await Task.Delay(PollDelayMs);
    activeToken = await ksefClient.GetKsefTokenAsync(tokenResponse.ReferenceNumber, accessToken);
    Console.Write(".");
    if (activeToken.Status == AuthenticationKsefTokenStatus.Active) break;
}
Console.WriteLine();

if (activeToken?.Status != AuthenticationKsefTokenStatus.Active)
{
    Console.Error.WriteLine($"  TOKEN ACTIVATION FAILED: status={activeToken?.Status}");
    Environment.Exit(1);
}

Console.WriteLine("[5/5] Token activated!");
Console.WriteLine();

const string OutputDir = "/app/output";
Directory.CreateDirectory(OutputDir);
await File.WriteAllTextAsync(Path.Combine(OutputDir, "test-token.txt"), tokenResponse.Token);
await File.WriteAllTextAsync(Path.Combine(OutputDir, "test-token.nip"), nip);

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  YOUR KSEF TEST TOKEN                                       ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"  KSEF_TOKEN={tokenResponse.Token}");
Console.WriteLine($"  KSEF_NIP={nip}");
Console.WriteLine($"  KSEF_ENV=TEST");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("Add these to your .env file to start the gateway, or read them from");
Console.WriteLine("./output/test-token.txt and ./output/test-token.nip.");

// Random NIP generator with valid checksum
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
