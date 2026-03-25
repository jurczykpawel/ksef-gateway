using System.Security.Cryptography.X509Certificates;
using System.Text;
using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Api.Builders.X509Certificates;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.Core.Models.Sessions.OnlineSession;
using KSeF.Client.DI;
using Microsoft.Extensions.DependencyInjection;

const string BaseUrl = "https://api-test.ksef.mf.gov.pl";
const int PollDelayMs = 3000;
const int MaxPollAttempts = 60;
const string OutputPath = "/app/output/invoice.xml";

string nip = GenerateRandomNip();
string buyerNip = GenerateRandomNip();
var today = DateTime.UtcNow;
string invoiceNum = today.ToString("HHmmss");

Console.WriteLine("==================================================");
Console.WriteLine("  KSeF Gateway - Invoice Demo (E2E)");
Console.WriteLine("==================================================");
Console.WriteLine($"  Environment: TEST");
Console.WriteLine($"  Seller NIP:  {nip}");
Console.WriteLine($"  Buyer NIP:   {buyerNip}");
Console.WriteLine("==================================================");
Console.WriteLine();

// ── Setup DI ──────────────────────────────────────────────────────────────────
var services = new ServiceCollection();
services.AddKSeFClient(options => { options.BaseUrl = BaseUrl; });
services.AddCryptographyClient();
var provider = services.BuildServiceProvider();

var authClient = provider.GetRequiredService<IAuthorizationClient>();
var ksefClient = provider.GetRequiredService<IKSeFClient>();
var cryptoService = provider.GetRequiredService<ICryptographyService>();

Console.Write("Waiting for crypto warmup...");
await cryptoService.WarmupAsync();
Console.WriteLine(" done.");
Console.WriteLine();

// ── Step 1: Authenticate (self-signed cert, TEST env) ─────────────────────────
Console.WriteLine("[1/9] Getting auth challenge...");
var challenge = await authClient.GetAuthChallengeAsync();
Console.WriteLine($"  Challenge: {challenge.Challenge[..20]}...");

Console.WriteLine("[2/9] Signing XAdES with self-signed certificate...");
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

Console.WriteLine("[3/9] Submitting auth request...");
var authOp = await authClient.SubmitXadesAuthRequestAsync(signedXml, verifyCertificateChain: false, enforceXadesCompliance: true);
Console.WriteLine($"  Reference: {authOp.ReferenceNumber}");

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

var tokens = await authClient.GetAccessTokenAsync(authOp.AuthenticationToken.Token);
string accessToken = tokens.AccessToken.Token;
Console.WriteLine($"  Access token obtained (expires: {tokens.AccessToken.ValidUntil})");
Console.WriteLine();

// ── Step 2: Build invoice XML ─────────────────────────────────────────────────
Console.WriteLine("[4/9] Building FA(3) invoice XML...");
string invoiceXml = BuildInvoiceXml(nip, buyerNip, today, invoiceNum);
Console.WriteLine($"  Invoice number: FV/DEMO/{invoiceNum}/{today:yyyy}");
Console.WriteLine($"  Invoice size: {Encoding.UTF8.GetByteCount(invoiceXml)} bytes");
Console.WriteLine();

// ── Step 3: Get encryption data and open online session ───────────────────────
Console.WriteLine("[5/9] Opening online session...");
EncryptionData encryptionData = cryptoService.GetEncryptionData();

OpenOnlineSessionRequest openReq = OpenOnlineSessionRequestBuilder
    .Create()
    .WithFormCode(systemCode: "FA (3)", schemaVersion: "1-0E", value: "FA")
    .WithEncryption(
        encryptedSymmetricKey: encryptionData.EncryptionInfo.EncryptedSymmetricKey,
        initializationVector: encryptionData.EncryptionInfo.InitializationVector)
    .Build();

var session = await ksefClient.OpenOnlineSessionAsync(openReq, accessToken);
Console.WriteLine($"  Session reference: {session.ReferenceNumber}");
Console.WriteLine();

// ── Step 4: Encrypt and send invoice ──────────────────────────────────────────
Console.WriteLine("[6/9] Encrypting and sending invoice...");
byte[] invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);
byte[] encrypted = cryptoService.EncryptBytesWithAES256(invoiceBytes, encryptionData.CipherKey, encryptionData.CipherIv);

var invoiceMeta = cryptoService.GetMetaData(invoiceBytes);
var encryptedMeta = cryptoService.GetMetaData(encrypted);

SendInvoiceRequest sendReq = SendInvoiceOnlineSessionRequestBuilder
    .Create()
    .WithInvoiceHash(invoiceMeta.HashSHA, invoiceMeta.FileSize)
    .WithEncryptedDocumentHash(encryptedMeta.HashSHA, encryptedMeta.FileSize)
    .WithEncryptedDocumentContent(Convert.ToBase64String(encrypted))
    .Build();

var sendResult = await ksefClient.SendOnlineSessionInvoiceAsync(sendReq, session.ReferenceNumber, accessToken);
Console.WriteLine($"  Invoice element reference: {sendResult.ElementReferenceNumber}");
Console.WriteLine();

// ── Step 5: Close session ─────────────────────────────────────────────────────
Console.WriteLine("[7/9] Closing session...");
await ksefClient.CloseOnlineSessionAsync(session.ReferenceNumber, accessToken);
Console.WriteLine("  Session closed.");
Console.WriteLine();

// ── Step 6: Poll for KSeF number ──────────────────────────────────────────────
Console.WriteLine("[8/9] Polling for KSeF number...");
string? ksefNumber = null;
for (int i = 0; i < MaxPollAttempts; i++)
{
    await Task.Delay(PollDelayMs);
    Console.Write(".");
    try
    {
        var invoiceStatus = await ksefClient.GetSessionInvoiceAsync(
            session.ReferenceNumber,
            sendResult.ElementReferenceNumber,
            accessToken);

        if (invoiceStatus?.KsefNumber is not null)
        {
            ksefNumber = invoiceStatus.KsefNumber;
            break;
        }
    }
    catch
    {
        // Invoice may not be ready yet, keep polling
    }
}
Console.WriteLine();

if (ksefNumber is null)
{
    Console.Error.WriteLine("  FAILED: KSeF number not assigned within timeout.");
    Environment.Exit(1);
}

Console.WriteLine($"  KSeF number: {ksefNumber}");
Console.WriteLine();

// ── Step 7: Download and save invoice XML ─────────────────────────────────────
Console.WriteLine("[9/9] Downloading invoice from KSeF...");
var downloadedXml = await ksefClient.DownloadInvoiceAsync(ksefNumber, accessToken);

// Ensure output directory exists
Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
await File.WriteAllTextAsync(OutputPath, downloadedXml);
Console.WriteLine($"  Saved to: {OutputPath}");
Console.WriteLine();

// ── Done ──────────────────────────────────────────────────────────────────────
Console.WriteLine("==================================================");
Console.WriteLine("  INVOICE DEMO COMPLETE");
Console.WriteLine("==================================================");
Console.WriteLine($"  KSeF Number:  {ksefNumber}");
Console.WriteLine($"  Invoice File: {OutputPath}");
Console.WriteLine("==================================================");

// ── Helpers ───────────────────────────────────────────────────────────────────

static string GenerateRandomNip()
{
    int[] weights = { 6, 5, 7, 2, 3, 4, 5, 6, 7 };
    var rng = new Random();
    int[] digits = new int[10];

    do
    {
        for (int i = 0; i < 9; i++)
            digits[i] = rng.Next(0, 10);

        if (digits[0] == 0) digits[0] = rng.Next(1, 10);

        int sum = 0;
        for (int i = 0; i < 9; i++)
            sum += digits[i] * weights[i];

        digits[9] = sum % 11;
    } while (digits[9] == 10);

    return string.Join("", digits);
}

static string BuildInvoiceXml(string sellerNip, string buyerNip, DateTime today, string invoiceNum)
{
    return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Faktura xmlns=""http://crd.gov.pl/wzor/2025/06/25/13775/"">
    <Naglowek>
        <KodFormularza kodSystemowy=""FA (3)"" wersjaSchemy=""1-0E"">FA</KodFormularza>
        <WariantFormularza>3</WariantFormularza>
        <DataWytworzeniaFa>{DateTime.UtcNow:O}</DataWytworzeniaFa>
        <SystemInfo>ksef-gateway-demo</SystemInfo>
    </Naglowek>
    <Podmiot1>
        <DaneIdentyfikacyjne>
            <NIP>{sellerNip}</NIP>
            <Nazwa>Test Company sp. z o.o.</Nazwa>
        </DaneIdentyfikacyjne>
        <Adres>
            <KodKraju>PL</KodKraju>
            <AdresL1>ul. Testowa 1</AdresL1>
            <AdresL2>00-001 Warszawa</AdresL2>
        </Adres>
    </Podmiot1>
    <Podmiot2>
        <DaneIdentyfikacyjne>
            <NIP>{buyerNip}</NIP>
            <Nazwa>Buyer Company sp. z o.o.</Nazwa>
        </DaneIdentyfikacyjne>
        <Adres>
            <KodKraju>PL</KodKraju>
            <AdresL1>ul. Kupiecka 2</AdresL1>
            <AdresL2>00-002 Warszawa</AdresL2>
        </Adres>
    </Podmiot2>
    <Fa>
        <KodWaluty>PLN</KodWaluty>
        <P_1>{today:yyyy-MM-dd}</P_1>
        <P_1M>Warszawa</P_1M>
        <P_2>FV/DEMO/{invoiceNum}/{today:yyyy}</P_2>
        <P_6>{today:yyyy-MM-dd}</P_6>
        <P_13_1>100.00</P_13_1>
        <P_14_1>23.00</P_14_1>
        <P_15>123.00</P_15>
        <Adnotacje>
            <P_16>2</P_16><P_17>2</P_17><P_18>2</P_18><P_18A>2</P_18A>
            <Zwolnienie><P_19N>1</P_19N></Zwolnienie>
            <NoweSrodkiTransportu><P_22N>1</P_22N></NoweSrodkiTransportu>
            <P_23>2</P_23>
            <PMarzy><P_PMarzyN>1</P_PMarzyN></PMarzy>
        </Adnotacje>
        <RodzajFaktury>VAT</RodzajFaktury>
        <FaWiersz>
            <NrWierszaFa>1</NrWierszaFa>
            <P_7>Usluga testowa ksef-gateway</P_7>
            <P_8A>szt.</P_8A>
            <P_8B>1</P_8B>
            <P_9A>100.00</P_9A>
            <P_11>100.00</P_11>
            <P_12>23</P_12>
        </FaWiersz>
        <Platnosc>
            <Zaplacono>1</Zaplacono>
            <DataZaplaty>{today:yyyy-MM-dd}</DataZaplaty>
            <FormaPlatnosci>6</FormaPlatnosci>
        </Platnosc>
    </Fa>
</Faktura>";
}
