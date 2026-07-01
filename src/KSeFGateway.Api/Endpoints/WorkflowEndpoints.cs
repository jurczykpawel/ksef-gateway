using System.Text;
using System.Text.Json;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Sessions.OnlineSession;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Invoice;
using KSeFGateway.Api.Middleware;
using KSeFGateway.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace KSeFGateway.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        // POST /ksef/send - accepts raw XML invoice
        app.MapPost("/ksef/send", async (HttpContext httpContext) =>
        {
            var pool = httpContext.RequestServices.GetRequiredService<TokenPool>();
            var ctxProvider = httpContext.RequestServices.GetRequiredService<ContextProvider>();
            var ksefClient = httpContext.RequestServices.GetRequiredService<IKSeFClient>();
            var cryptoService = httpContext.RequestServices.GetRequiredService<ICryptographyService>();
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();

            string invoiceXml;
            using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8))
                invoiceXml = await reader.ReadToEndAsync(httpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(invoiceXml))
                return Results.Json(ApiResponse.Fail("Empty request body. Send FA(3) XML."), statusCode: 400);

            if (!invoiceXml.TrimStart().StartsWith("<?xml") && !invoiceXml.TrimStart().StartsWith("<Faktura"))
                return Results.Json(ApiResponse.Fail("Body must be FA(3) XML."), statusCode: 400);

            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider, invoiceXml);
            if (nip is null)
                return Results.Json(ApiResponse.Fail("Cannot determine NIP. Set X-KSeF-NIP header or include seller NIP in invoice."), statusCode: 400);

            var accessToken = await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted);
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail($"Not authenticated with KSeF for NIP {nip}"), statusCode: 503);

            return await SendInvoiceXml(invoiceXml, accessToken, ksefClient, cryptoService, logger, httpContext.RequestAborted);
        })
        .WithTags("Workflows")
        .WithName("send_invoice")
        .WithOpenApi()
        .Accepts<string>("application/xml");

        // GET /ksef/invoice/{ksefNumber} - download invoice XML
        app.MapGet("/ksef/invoice/{ksefNumber}", async (
            string ksefNumber,
            [FromServices] IKSeFClient ksefClient,
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            HttpContext httpContext) =>
        {
            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider) ?? ksefNumber.Split('-').FirstOrDefault();
            var accessToken = nip != null ? await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted) : null;
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail("Not authenticated"), statusCode: 503);

            var xml = await ksefClient.GetInvoiceAsync(ksefNumber, accessToken);
            return Results.Content(xml, "application/xml");
        })
        .WithTags("Workflows")
        .WithName("get_invoice")
        .WithOpenApi();

        // GET /ksef/invoice/{ksefNumber}/pdf
        app.MapGet("/ksef/invoice/{ksefNumber}/pdf", async (
            string ksefNumber,
            [FromServices] IKSeFClient ksefClient,
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            [FromServices] IConfiguration config,
            [FromServices] IHttpClientFactory httpClientFactory,
            HttpContext httpContext) =>
        {
            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider) ?? ksefNumber.Split('-').FirstOrDefault();
            var accessToken = nip != null ? await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted) : null;
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail("Not authenticated"), statusCode: 503);

            return await EndpointErrorHandling.Guard(async () =>
            {
                var xml = await ksefClient.GetInvoiceAsync(ksefNumber, accessToken);
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
            });
        })
        .WithTags("Workflows")
        .WithName("get_invoice_pdf")
        .WithOpenApi();

        // POST /ksef/send/json - xml-js compact format
        app.MapPost("/ksef/send/json", async (HttpContext httpContext) =>
        {
            var pool = httpContext.RequestServices.GetRequiredService<TokenPool>();
            var ctxProvider = httpContext.RequestServices.GetRequiredService<ContextProvider>();
            var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
            var httpClientFactory = httpContext.RequestServices.GetRequiredService<IHttpClientFactory>();

            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            var jsonBody = await reader.ReadToEndAsync(httpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(jsonBody))
                return Results.Json(ApiResponse.Fail("Empty request body"), statusCode: 400);

            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider, jsonBody);
            if (nip is null)
                return Results.Json(ApiResponse.Fail("Cannot determine NIP. Set X-KSeF-NIP header or include seller NIP in JSON."), statusCode: 400);

            var accessToken = await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted);
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail($"Not authenticated with KSeF for NIP {nip}"), statusCode: 503);

            return await EndpointErrorHandling.Guard(async () =>
            {
                var pdfServiceUrl = config["PDF_SERVICE_URL"] ?? "http://ksef-pdf:3000";
                var client = httpClientFactory.CreateClient();

                var convertRequest = new HttpRequestMessage(HttpMethod.Post, $"{pdfServiceUrl}/json-to-xml");
                convertRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var convertResponse = await client.SendAsync(convertRequest, httpContext.RequestAborted);
                if (!convertResponse.IsSuccessStatusCode)
                {
                    var error = await convertResponse.Content.ReadAsStringAsync(httpContext.RequestAborted);
                    return Results.Json(ApiResponse.Fail($"JSON to XML conversion failed: {error}"), statusCode: 400);
                }

                var invoiceXml = await convertResponse.Content.ReadAsStringAsync(httpContext.RequestAborted);
                var sendRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:8080/ksef/send");
                sendRequest.Headers.Add(ContextResolver.NipHeader, nip);
                sendRequest.Content = new StringContent(invoiceXml, Encoding.UTF8, "application/xml");

                var sendResponse = await client.SendAsync(sendRequest, httpContext.RequestAborted);
                var sendResult = await sendResponse.Content.ReadAsStringAsync(httpContext.RequestAborted);

                httpContext.Response.ContentType = "application/json";
                httpContext.Response.StatusCode = (int)sendResponse.StatusCode;
                await httpContext.Response.WriteAsync(sendResult, httpContext.RequestAborted);
                return Results.Empty;
            });
        })
        .WithTags("Workflows")
        .WithName("send_invoice_json")
        .WithOpenApi();

        // POST /ksef/invoice - friendly JSON
        app.MapPost("/ksef/invoice", async (HttpContext httpContext) =>
        {
            var pool = httpContext.RequestServices.GetRequiredService<TokenPool>();
            var ctxProvider = httpContext.RequestServices.GetRequiredService<ContextProvider>();
            var ksefClient = httpContext.RequestServices.GetRequiredService<IKSeFClient>();
            var cryptoService = httpContext.RequestServices.GetRequiredService<ICryptographyService>();
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();

            InvoiceRequest invoiceReq;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(httpContext.RequestAborted);
                invoiceReq = JsonSerializer.Deserialize<InvoiceRequest>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Empty request body");
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse.Fail($"Invalid invoice JSON: {ex.Message}"), statusCode: 400);
            }

            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider) ?? invoiceReq.Seller.Nip;
            var accessToken = await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted);
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail($"Not authenticated with KSeF for NIP {nip}"), statusCode: 503);

            return await EndpointErrorHandling.Guard(async () =>
            {
                var invoiceXml = InvoiceXmlBuilder.Build(invoiceReq);
                var invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);
                logger.LogInformation("Built FA(3) XML ({Size} bytes) for NIP {Nip}", invoiceBytes.Length, nip);

                return await SendInvoiceXml(invoiceXml, accessToken, ksefClient, cryptoService, logger, httpContext.RequestAborted);
            }, logger, $"Invoice send failed for NIP {nip}");
        })
        .WithTags("Workflows")
        .WithName("send_invoice_friendly")
        .WithOpenApi();

        // GET /ksef/contexts - list configured contexts and their auth status
        app.MapGet("/ksef/contexts", (
            [FromServices] ContextProvider ctxProvider,
            [FromServices] TokenPool pool) =>
        {
            var contexts = ctxProvider.GetAll().Select(c =>
            {
                var state = pool.GetState(c.Nip);
                return new
                {
                    c.Nip,
                    c.Label,
                    authenticated = state.IsAuthenticated,
                    tokenExpiresAt = state.AccessTokenExpiresAt,
                    isDefault = c.Nip == ctxProvider.GetDefault()?.Nip
                };
            });
            return Results.Json(ApiResponse.Ok(contexts));
        })
        .WithTags("System")
        .WithName("list_contexts")
        .WithOpenApi();
    }

    /// <summary>
    /// Shared logic: encrypt + send XML invoice via online session.
    /// </summary>
    private static async Task<IResult> SendInvoiceXml(
        string invoiceXml,
        string accessToken,
        IKSeFClient ksefClient,
        ICryptographyService cryptoService,
        ILogger logger,
        CancellationToken ct)
    {
        return await EndpointErrorHandling.Guard(async () =>
        {
            var invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);
            var encryptionData = cryptoService.GetEncryptionData();

            var openReq = OpenOnlineSessionRequestBuilder
                .Create()
                .WithFormCode(systemCode: "FA (3)", schemaVersion: "1-0E", value: "FA")
                .WithEncryption(
                    encryptedSymmetricKey: encryptionData.EncryptionInfo.EncryptedSymmetricKey,
                    initializationVector: encryptionData.EncryptionInfo.InitializationVector)
                .Build();

            var session = await ksefClient.OpenOnlineSessionAsync(openReq, accessToken);
            logger.LogInformation("Session opened: {Ref}", session.ReferenceNumber);

            var encrypted = cryptoService.EncryptBytesWithAES256(
                invoiceBytes, encryptionData.CipherKey, encryptionData.CipherIv);
            var invoiceMeta = cryptoService.GetMetaData(invoiceBytes);
            var encryptedMeta = cryptoService.GetMetaData(encrypted);

            var sendReq = SendInvoiceOnlineSessionRequestBuilder
                .Create()
                .WithInvoiceHash(invoiceMeta.HashSHA, invoiceMeta.FileSize)
                .WithEncryptedDocumentHash(encryptedMeta.HashSHA, encryptedMeta.FileSize)
                .WithEncryptedDocumentContent(Convert.ToBase64String(encrypted))
                .Build();

            var sendResult = await ksefClient.SendOnlineSessionInvoiceAsync(
                sendReq, session.ReferenceNumber, accessToken);

            await ksefClient.CloseOnlineSessionAsync(session.ReferenceNumber, accessToken);

            string? ksefNumber = null;
            string? statusDescription = null;
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(2000, ct);
                try
                {
                    var invoiceStatus = await ksefClient.GetSessionInvoiceAsync(
                        session.ReferenceNumber, sendResult.ReferenceNumber, accessToken);
                    statusDescription = invoiceStatus?.Status?.Description;
                    if (invoiceStatus?.KsefNumber is not null)
                    {
                        ksefNumber = invoiceStatus.KsefNumber;
                        break;
                    }
                    // KSeF returned a status with an error description - invoice rejected
                    if (statusDescription is not null)
                        break;
                }
                catch { }
            }

            if (ksefNumber is null)
            {
                var reason = statusDescription ?? "KSeF did not assign a reference number within the timeout period.";
                logger.LogWarning("Invoice rejected or timed out: {Reason}", reason);
                return Results.Json(ApiResponse.Fail($"KSeF rejected invoice: {reason}"), statusCode: 502);
            }

            return Results.Json(ApiResponse.Ok(new
            {
                ksefNumber,
                status = "accepted",
                statusDescription,
                sessionReferenceNumber = session.ReferenceNumber,
                invoiceReferenceNumber = sendResult.ReferenceNumber
            }));
        }, logger, "Invoice send failed");
    }
}
