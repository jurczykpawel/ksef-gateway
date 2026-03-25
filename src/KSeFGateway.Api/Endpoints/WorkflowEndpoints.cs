using System.Text;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Sessions.OnlineSession;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace KSeFGateway.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        // POST /ksef/send - accepts raw XML invoice, handles everything else
        app.MapPost("/ksef/send", async (HttpContext httpContext) =>
        {
            var tokenManager = httpContext.RequestServices.GetRequiredService<TokenManager>();
            var ksefClient = httpContext.RequestServices.GetRequiredService<IKSeFClient>();
            var cryptoService = httpContext.RequestServices.GetRequiredService<ICryptographyService>();
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();

            var accessToken = tokenManager.GetCurrentAccessToken();
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail("Not authenticated with KSeF"), statusCode: 503);

            // Read invoice XML from body
            string invoiceXml;
            using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8))
            {
                invoiceXml = await reader.ReadToEndAsync(httpContext.RequestAborted);
            }

            if (string.IsNullOrWhiteSpace(invoiceXml))
                return Results.Json(ApiResponse.Fail("Empty request body. Send FA(3) XML."), statusCode: 400);

            if (!invoiceXml.TrimStart().StartsWith("<?xml") && !invoiceXml.TrimStart().StartsWith("<Faktura"))
                return Results.Json(ApiResponse.Fail("Body must be FA(3) XML. Set Content-Type: application/xml"), statusCode: 400);

            try
            {
                var invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);

                // 1. Get encryption materials
                logger.LogInformation("Generating encryption materials...");
                var encryptionData = cryptoService.GetEncryptionData();

                // 2. Open online session
                logger.LogInformation("Opening online session...");
                var openReq = OpenOnlineSessionRequestBuilder
                    .Create()
                    .WithFormCode(systemCode: "FA (3)", schemaVersion: "1-0E", value: "FA")
                    .WithEncryption(
                        encryptedSymmetricKey: encryptionData.EncryptionInfo.EncryptedSymmetricKey,
                        initializationVector: encryptionData.EncryptionInfo.InitializationVector)
                    .Build();

                var session = await ksefClient.OpenOnlineSessionAsync(openReq, accessToken);
                logger.LogInformation("Session opened: {Ref}", session.ReferenceNumber);

                // 3. Encrypt invoice
                var encrypted = cryptoService.EncryptBytesWithAES256(
                    invoiceBytes, encryptionData.CipherKey, encryptionData.CipherIv);
                var invoiceMeta = cryptoService.GetMetaData(invoiceBytes);
                var encryptedMeta = cryptoService.GetMetaData(encrypted);

                // 4. Send invoice
                logger.LogInformation("Sending invoice...");
                var sendReq = SendInvoiceOnlineSessionRequestBuilder
                    .Create()
                    .WithInvoiceHash(invoiceMeta.HashSHA, invoiceMeta.FileSize)
                    .WithEncryptedDocumentHash(encryptedMeta.HashSHA, encryptedMeta.FileSize)
                    .WithEncryptedDocumentContent(Convert.ToBase64String(encrypted))
                    .Build();

                var sendResult = await ksefClient.SendOnlineSessionInvoiceAsync(
                    sendReq, session.ReferenceNumber, accessToken);
                logger.LogInformation("Invoice sent: {Ref}", sendResult.ReferenceNumber);

                // 5. Close session
                logger.LogInformation("Closing session...");
                await ksefClient.CloseOnlineSessionAsync(session.ReferenceNumber, accessToken);

                // 6. Poll for KSeF number (max 60s)
                logger.LogInformation("Waiting for KSeF number...");
                string? ksefNumber = null;
                string? statusDescription = null;
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(2000, httpContext.RequestAborted);
                    try
                    {
                        var invoiceStatus = await ksefClient.GetSessionInvoiceAsync(
                            session.ReferenceNumber,
                            sendResult.ReferenceNumber,
                            accessToken);

                        statusDescription = invoiceStatus?.Status?.Description;

                        if (invoiceStatus?.KsefNumber is not null)
                        {
                            ksefNumber = invoiceStatus.KsefNumber;
                            logger.LogInformation("KSeF number assigned: {KsefNumber}", ksefNumber);
                            break;
                        }
                    }
                    catch
                    {
                        // Status not ready yet
                    }
                }

                return Results.Json(ApiResponse.Ok(new
                {
                    ksefNumber,
                    status = ksefNumber is not null ? "accepted" : "pending",
                    statusDescription,
                    sessionReferenceNumber = session.ReferenceNumber,
                    invoiceReferenceNumber = sendResult.ReferenceNumber
                }));
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException is not null
                    ? $"{ex.Message} -> {ex.InnerException.Message}"
                    : ex.Message;
                logger.LogError(ex, "Invoice send failed");
                return Results.Json(ApiResponse.Fail(msg), statusCode: 500);
            }
        })
        .WithTags("Workflows")
        .WithName("send_invoice")
        .WithOpenApi()
        .Accepts<string>("application/xml");

        // GET /ksef/invoice/{ksefNumber} - download invoice XML
        app.MapGet("/ksef/invoice/{ksefNumber}", async (
            string ksefNumber,
            [FromServices] IKSeFClient ksefClient,
            [FromServices] TokenManager tokenManager) =>
        {
            var accessToken = tokenManager.GetCurrentAccessToken();
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail("Not authenticated"), statusCode: 503);

            try
            {
                var xml = await ksefClient.GetInvoiceAsync(ksefNumber, accessToken);
                return Results.Content(xml, "application/xml");
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse.Fail(ex.Message), statusCode: 500);
            }
        })
        .WithTags("Workflows")
        .WithName("get_invoice")
        .WithOpenApi();

        // GET /ksef/invoice/{ksefNumber}/pdf - download invoice as PDF with QR
        app.MapGet("/ksef/invoice/{ksefNumber}/pdf", async (
            string ksefNumber,
            [FromServices] IKSeFClient ksefClient,
            [FromServices] TokenManager tokenManager,
            [FromServices] IConfiguration config,
            [FromServices] IHttpClientFactory httpClientFactory) =>
        {
            var accessToken = tokenManager.GetCurrentAccessToken();
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail("Not authenticated"), statusCode: 503);

            try
            {
                // 1. Download XML from KSeF
                var xml = await ksefClient.GetInvoiceAsync(ksefNumber, accessToken);

                // 2. Send to PDF service with KSeF number (triggers QR generation)
                var pdfServiceUrl = config["PDF_SERVICE_URL"] ?? "http://ksef-pdf:3000";
                var client = httpClientFactory.CreateClient();
                var pdfRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"{pdfServiceUrl}/pdf/invoice?nrKSeF={Uri.EscapeDataString(ksefNumber)}");
                pdfRequest.Content = new StringContent(xml, Encoding.UTF8, "application/xml");

                var pdfResponse = await client.SendAsync(pdfRequest);
                if (!pdfResponse.IsSuccessStatusCode)
                {
                    var error = await pdfResponse.Content.ReadAsStringAsync();
                    return Results.Json(ApiResponse.Fail($"PDF generation failed: {error}"), statusCode: 502);
                }

                var pdfBytes = await pdfResponse.Content.ReadAsByteArrayAsync();
                return Results.File(pdfBytes, "application/pdf", $"faktura-{ksefNumber}.pdf");
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse.Fail(ex.Message), statusCode: 500);
            }
        })
        .WithTags("Workflows")
        .WithName("get_invoice_pdf")
        .WithOpenApi();
    }
}
