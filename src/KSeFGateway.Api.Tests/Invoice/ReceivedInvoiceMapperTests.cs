using KSeF.Client.Core.Models.Invoices;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class ReceivedInvoiceMapperTests
{
    private static InvoiceSummary MakeInvoice(string ksefNumber, DateTimeOffset permanentStorageDate) => new()
    {
        KsefNumber = ksefNumber,
        InvoiceNumber = $"FV/{ksefNumber}",
        IssueDate = permanentStorageDate,
        InvoicingDate = permanentStorageDate,
        AcquisitionDate = permanentStorageDate,
        PermanentStorageDate = permanentStorageDate,
        Seller = new Seller { Nip = "1234567890", Name = "Seller sp. z o.o." },
        Buyer = new Buyer
        {
            Identifier = new BuyerIdentifier { Type = BuyerIdentifierType.Nip, Value = "0987654321" },
            Name = "Buyer sp. z o.o.",
        },
        NetAmount = 100m,
        GrossAmount = 123m,
        VatAmount = 23m,
        Currency = "PLN",
        InvoicingMode = InvoicingMode.Online,
        InvoiceType = KSeF.Client.Core.Models.Invoices.Common.InvoiceType.Vat,
        FormCode = new KSeF.Client.Core.Models.Sessions.FormCode
        {
            SystemCode = "FA (3)",
            SchemaVersion = "1-0E",
            Value = "FA",
        },
        IsSelfInvoicing = false,
        HasAttachment = false,
    };

    private static PagedInvoiceResponse MakePage(
        IEnumerable<InvoiceSummary> invoices, bool hasMore, DateTimeOffset? hwmDate) => new()
    {
        HasMore = hasMore,
        IsTruncated = false,
        Invoices = invoices.ToList(),
        PermanentStorageHwmDate = hwmDate,
    };

    [Fact]
    public void ToSummary_MapsAllFields()
    {
        var storedAt = DateTimeOffset.Parse("2026-07-01T06:31:10Z");
        var invoice = MakeInvoice("1234567890-20260701-3BE6FF400000-63", storedAt);

        var summary = ReceivedInvoiceMapper.ToSummary(invoice);

        Assert.Equal(invoice.KsefNumber, summary.KsefNumber);
        Assert.Equal(invoice.InvoiceNumber, summary.InvoiceNumber);
        Assert.Equal(storedAt, summary.PermanentStorageDate);
        Assert.Equal("1234567890", summary.SellerNip);
        Assert.Equal("Seller sp. z o.o.", summary.SellerName);
        Assert.Equal(100m, summary.NetAmount);
        Assert.Equal(123m, summary.GrossAmount);
        Assert.Equal(23m, summary.VatAmount);
        Assert.Equal("PLN", summary.Currency);
        Assert.False(summary.HasAttachment);
    }

    [Fact]
    public void AggregateForPolling_NotTruncated_UsesQueryLevelHwmDate()
    {
        var hwm = DateTimeOffset.Parse("2026-07-01T07:00:00Z");
        var pages = new[]
        {
            MakePage([MakeInvoice("A", DateTimeOffset.Parse("2026-07-01T06:00:00Z"))], hasMore: false, hwmDate: hwm),
        };

        var (invoices, nextSince) = ReceivedInvoiceMapper.AggregateForPolling(
            pages, truncated: false, fallbackSince: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

        Assert.Single(invoices);
        Assert.Equal(hwm, nextSince);
    }

    [Fact]
    public void AggregateForPolling_Truncated_ResumesFromLatestSeenInvoiceInsteadOfHwm()
    {
        // The query-level HWM might be later than what we actually retrieved when truncated -
        // jumping straight to it could silently skip invoices we never fetched.
        var hwm = DateTimeOffset.Parse("2026-07-01T09:00:00Z");
        var latestSeen = DateTimeOffset.Parse("2026-07-01T07:00:00Z");
        var pages = new[]
        {
            MakePage(
                [
                    MakeInvoice("A", DateTimeOffset.Parse("2026-07-01T06:00:00Z")),
                    MakeInvoice("B", latestSeen),
                ],
                hasMore: true,
                hwmDate: hwm),
        };

        var (invoices, nextSince) = ReceivedInvoiceMapper.AggregateForPolling(
            pages, truncated: true, fallbackSince: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

        Assert.Equal(2, invoices.Count);
        Assert.Equal(latestSeen, nextSince);
        Assert.NotEqual(hwm, nextSince);
    }

    [Fact]
    public void AggregateForPolling_TruncatedWithNoInvoices_FallsBackToProvidedSince()
    {
        var fallback = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var pages = new[] { MakePage([], hasMore: true, hwmDate: null) };

        var (invoices, nextSince) = ReceivedInvoiceMapper.AggregateForPolling(
            pages, truncated: true, fallbackSince: fallback);

        Assert.Empty(invoices);
        Assert.Equal(fallback, nextSince);
    }

    [Fact]
    public void AggregateForPolling_MultiplePages_ConcatenatesInOrder()
    {
        var pages = new[]
        {
            MakePage([MakeInvoice("A", DateTimeOffset.Parse("2026-07-01T06:00:00Z"))], hasMore: true, hwmDate: null),
            MakePage([MakeInvoice("B", DateTimeOffset.Parse("2026-07-01T06:30:00Z"))], hasMore: false, hwmDate: DateTimeOffset.Parse("2026-07-01T07:00:00Z")),
        };

        var (invoices, nextSince) = ReceivedInvoiceMapper.AggregateForPolling(
            pages, truncated: false, fallbackSince: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

        Assert.Equal(["A", "B"], invoices.Select(i => i.KsefNumber));
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T07:00:00Z"), nextSince);
    }

    [Fact]
    public void AggregateForPolling_NoPages_FallsBackToNow()
    {
        var (invoices, nextSince) = ReceivedInvoiceMapper.AggregateForPolling(
            [], truncated: false, fallbackSince: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

        Assert.Empty(invoices);
        Assert.True(nextSince > DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
