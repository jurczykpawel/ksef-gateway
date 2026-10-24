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
        .WithName("get_invoice");

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
                var pdfServiceUrl = PdfService.BaseUrl(config);
                var client = httpClientFactory.CreateClient();
                var pdfRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"{pdfServiceUrl}/pdf/invoice?nrKSeF={Uri.EscapeDataString(ksefNumber)}");
                PdfService.Authorize(pdfRequest, config);
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
        .WithName("get_invoice_pdf");

        // POST /ksef/send/json - xml-js compact format
        app.MapPost("/ksef/send/json", async (HttpContext httpContext) =>
        {
            var pool = httpContext.RequestServices.GetRequiredService<TokenPool>();
            var ctxProvider = httpContext.RequestServices.GetRequiredService<ContextProvider>();
            var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
            var httpClientFactory = httpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
            var ksefClient = httpContext.RequestServices.GetRequiredService<IKSeFClient>();
            var cryptoService = httpContext.RequestServices.GetRequiredService<ICryptographyService>();
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();

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
                var pdfServiceUrl = PdfService.BaseUrl(config);
                var client = httpClientFactory.CreateClient();

                var convertRequest = new HttpRequestMessage(HttpMethod.Post, $"{pdfServiceUrl}/json-to-xml");
                PdfService.Authorize(convertRequest, config);
                convertRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var convertResponse = await client.SendAsync(convertRequest, httpContext.RequestAborted);
                if (!convertResponse.IsSuccessStatusCode)
                {
                    var error = await convertResponse.Content.ReadAsStringAsync(httpContext.RequestAborted);
                    return Results.Json(ApiResponse.Fail($"JSON to XML conversion failed: {error}"), statusCode: 400);
                }

                var invoiceXml = await convertResponse.Content.ReadAsStringAsync(httpContext.RequestAborted);

                // Same code path as POST /ksef/send. This used to call the gateway's own
                // http://localhost:8080/ksef/send without X-Api-Key, so the API-key middleware
                // rejected it with 401 on every request (and it broke on any non-8080 port).
                return await SendInvoiceXml(invoiceXml, accessToken, ksefClient, cryptoService, logger, httpContext.RequestAborted);
            }, logger, $"JSON invoice send failed for NIP {nip}");
        })
        .WithTags("Workflows")
        .WithName("send_invoice_json");

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
        .WithName("send_invoice_friendly");

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
        .WithName("list_contexts");
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
        CancellationToken ct,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
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
                    initializationVector: encryptionData.EncryptionInfo.InitializationVector,
                    publicKeyId: encryptionData.EncryptionInfo.PublicKeyId)
                .Build();

            var session = await ksefClient.OpenOnlineSessionAsync(openReq, accessToken, cancellationToken: ct);
            logger.LogInformation("Session opened: {Ref}", session.ReferenceNumber);

            try
            {
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
                    sendReq, session.ReferenceNumber, accessToken, ct);

                timeProvider ??= TimeProvider.System;
                delay ??= (duration, token) => Task.Delay(duration, timeProvider, token);
                var timeout = TimeSpan.FromSeconds(60);
                var started = timeProvider.GetTimestamp();
                using var deadline = new CancellationTokenSource(timeout, timeProvider);
                using var polling = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
                try
                {
                    while (true)
                    {
                        var remaining = timeout - timeProvider.GetElapsedTime(started);
                        if (remaining <= TimeSpan.Zero)
                            break;
                        await delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2), polling.Token);
                        if (timeProvider.GetElapsedTime(started) >= timeout)
                            break;

                        var invoiceStatus = await ksefClient.GetSessionInvoiceAsync(
                            session.ReferenceNumber, sendResult.ReferenceNumber, accessToken, polling.Token);
                        ct.ThrowIfCancellationRequested();
                        if (timeProvider.GetElapsedTime(started) >= timeout)
                            break;

                        // Invoice status codes, not session status codes:
                        // KSeF OpenAPI: SessionInvoiceStatusResponse.status.
                        switch (invoiceStatus?.Status?.Code)
                        {
                            case null:
                            case 100: // Accepted for processing
                            case 150: // Processing
                                continue;
                            case 200:
                                if (string.IsNullOrWhiteSpace(invoiceStatus.KsefNumber))
                                    continue;
                                return Results.Json(ApiResponse.Ok(new
                                {
                                    ksefNumber = invoiceStatus.KsefNumber,
                                    status = "accepted",
                                    statusDescription = invoiceStatus.Status.Description,
                                    sessionReferenceNumber = session.ReferenceNumber,
                                    invoiceReferenceNumber = sendResult.ReferenceNumber
                                }));
                            default:
                                // Includes documented failures (405, 410, 415, 430, 435,
                                // 440, 450, 500, 550) and fails closed for unknown codes.
                                return Results.Json(ApiResponse.Fail(
                                    $"KSeF rejected invoice (status {invoiceStatus.Status.Code}): {invoiceStatus.Status.Description}"),
                                    statusCode: 502);
                        }
                    }
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // Only our polling deadline becomes a timeout response. Caller cancellation propagates.
                }

                ct.ThrowIfCancellationRequested();
                return Results.Json(ApiResponse.Fail(
                    "KSeF invoice status timeout after 60 seconds; acceptance is unknown. Check status before resending."),
                    statusCode: 502);
            }
            finally
            {
                try
                {
                    await ksefClient.CloseOnlineSessionAsync(session.ReferenceNumber, accessToken, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not close KSeF session {Ref}", session.ReferenceNumber);
                }
            }
        }, logger, "Invoice send failed");
    }
}
