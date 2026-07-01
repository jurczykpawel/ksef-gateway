using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Invoices;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Invoice;
using KSeFGateway.Api.Middleware;
using KSeFGateway.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace KSeFGateway.Api.Endpoints;

public static class InvoiceDownloadEndpoints
{
    private const int DefaultLookbackDays = 30;
    private const int DefaultPageSize = 50;
    private const int PollPageSize = 100;

    // KSeF's query/metadata endpoint is rate-limited to 8 req/s and 20 req/h (see limity-api.md).
    // A single poll call fans out into up to this many internal page fetches, so keep it small -
    // high-invoice-volume accounts should use the batch export mechanism instead (not yet wrapped
    // here). A short delay between pages keeps a multi-page poll comfortably under the per-second cap.
    private const int MaxPollPages = 5;
    private static readonly TimeSpan InterPageDelay = TimeSpan.FromMilliseconds(200);

    public static void MapInvoiceDownloadEndpoints(this WebApplication app)
    {
        // GET /ksef/invoices/received - browse invoices where the caller is the buyer
        app.MapGet("/ksef/invoices/received", async (
            HttpContext httpContext,
            [FromServices] IKSeFClient ksefClient,
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            [FromServices] ILogger<Program> logger,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int page = 0,
            int pageSize = DefaultPageSize) =>
        {
            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider);
            if (nip is null)
                return Results.Json(ApiResponse.Fail("No KSeF context. Set X-KSeF-NIP header or configure default."), statusCode: 400);

            var accessToken = await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted);
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail($"Not authenticated with KSeF for NIP {nip}"), statusCode: 503);

            return await EndpointErrorHandling.Guard(async () =>
            {
                var filters = new InvoiceQueryFilters
                {
                    SubjectType = InvoiceSubjectType.Subject2,
                    DateRange = new DateRange
                    {
                        DateType = DateType.Issue,
                        From = from ?? DateTimeOffset.UtcNow.AddDays(-DefaultLookbackDays),
                        To = to ?? DateTimeOffset.UtcNow,
                    },
                };

                var result = await ksefClient.QueryInvoiceMetadataAsync(
                    filters, accessToken, pageOffset: page, pageSize: pageSize,
                    cancellationToken: httpContext.RequestAborted);

                return Results.Json(ApiResponse.Ok(new
                {
                    invoices = result.Invoices.Select(ReceivedInvoiceMapper.ToSummary),
                    hasMore = result.HasMore,
                }));
            }, logger, $"List received invoices failed for NIP {nip}");
        })
        .WithTags("Workflows")
        .WithName("list_received_invoices")
        .WithOpenApi();

        // GET /ksef/invoices/received/new - poll for invoices received since a checkpoint
        // (returned as nextSince; pass it back as ?since= on the next call for continuous sync)
        app.MapGet("/ksef/invoices/received/new", async (
            HttpContext httpContext,
            [FromServices] IKSeFClient ksefClient,
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            [FromServices] ILogger<Program> logger,
            DateTimeOffset? since) =>
        {
            var nip = ContextResolver.ResolveNip(httpContext, ctxProvider);
            if (nip is null)
                return Results.Json(ApiResponse.Fail("No KSeF context. Set X-KSeF-NIP header or configure default."), statusCode: 400);

            var accessToken = await pool.GetAccessTokenAsync(nip, httpContext.RequestAborted);
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail($"Not authenticated with KSeF for NIP {nip}"), statusCode: 503);

            var from = since ?? DateTimeOffset.UtcNow.AddDays(-DefaultLookbackDays);

            return await EndpointErrorHandling.Guard(async () =>
            {
                var pages = new List<PagedInvoiceResponse>();
                var truncated = false;

                for (var pageOffset = 0; pageOffset < MaxPollPages; pageOffset++)
                {
                    if (pageOffset > 0)
                        await Task.Delay(InterPageDelay, httpContext.RequestAborted);

                    var filters = new InvoiceQueryFilters
                    {
                        SubjectType = InvoiceSubjectType.Subject2,
                        DateRange = new DateRange
                        {
                            DateType = DateType.PermanentStorage,
                            From = from,
                            RestrictToPermanentStorageHwmDate = true,
                        },
                    };

                    var page = await ksefClient.QueryInvoiceMetadataAsync(
                        filters, accessToken, pageOffset: pageOffset, pageSize: PollPageSize,
                        cancellationToken: httpContext.RequestAborted);

                    pages.Add(page);

                    if (!page.HasMore)
                        break;
                    if (pageOffset == MaxPollPages - 1)
                        truncated = true;
                }

                var (invoices, nextSince) = ReceivedInvoiceMapper.AggregateForPolling(pages, truncated, from);

                return Results.Json(ApiResponse.Ok(new
                {
                    invoices,
                    hasMore = truncated,
                    nextSince,
                }));
            }, logger, $"List new received invoices failed for NIP {nip}");
        })
        .WithTags("Workflows")
        .WithName("list_new_received_invoices")
        .WithOpenApi();
    }
}
