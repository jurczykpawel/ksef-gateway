using KSeF.Client.Core.Models.Invoices;

namespace KSeFGateway.Api.Invoice;

public static class ReceivedInvoiceMapper
{
    public static ReceivedInvoiceSummary ToSummary(InvoiceSummary invoice) => new(
        invoice.KsefNumber,
        invoice.InvoiceNumber,
        invoice.IssueDate,
        invoice.PermanentStorageDate,
        invoice.Seller.Nip,
        invoice.Seller.Name,
        invoice.NetAmount,
        invoice.GrossAmount,
        invoice.VatAmount,
        invoice.Currency,
        invoice.HasAttachment
    );

    /// <summary>
    /// Aggregates paginated query-metadata pages into a flat invoice list plus the
    /// checkpoint to resume from on the next poll.
    ///
    /// When the full result set was retrieved (<paramref name="truncated"/> is false), the
    /// checkpoint is the query's own PermanentStorageHwmDate - the stable point up to which
    /// KSeF guarantees completeness.
    ///
    /// When truncated (more pages existed than we fetched in this call), jumping straight to
    /// the query-level HWM would risk skipping invoices we never actually retrieved. Instead
    /// resume from the latest PermanentStorageDate we actually saw; the same boundary
    /// invoice(s) may reappear on the next poll - callers should dedupe by KsefNumber, same as
    /// KSeF's own recommended pattern for truncated export packages.
    /// </summary>
    public static (IReadOnlyList<ReceivedInvoiceSummary> Invoices, DateTimeOffset NextSince) AggregateForPolling(
        IEnumerable<PagedInvoiceResponse> pages,
        bool truncated,
        DateTimeOffset fallbackSince)
    {
        var invoices = new List<ReceivedInvoiceSummary>();
        DateTimeOffset? hwmDate = null;

        foreach (var page in pages)
        {
            invoices.AddRange(page.Invoices.Select(ToSummary));
            hwmDate = page.PermanentStorageHwmDate ?? hwmDate;
        }

        var nextSince = truncated
            ? (invoices.Count > 0 ? invoices.Max(i => i.PermanentStorageDate) : fallbackSince)
            : (hwmDate ?? DateTimeOffset.UtcNow);

        return (invoices, nextSince);
    }
}
