using System.Text;
using ECommerceStore.Web.Services.Invoices;

namespace ECommerceStore.Tests.Services;

public sealed class InvoicePdfServiceTests
{
    internal static InvoiceModel SampleInvoice(string orderNumber = "ORD-20260710-abc123", string invoiceNumber = "INV-20260710-def456") => new(
        StoreName: "ECommerceStore",
        OrderNumber: orderNumber,
        InvoiceNumber: invoiceNumber,
        CreatedAtUtc: new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        CustomerName: "Jane Buyer",
        CustomerEmail: "jane@example.test",
        AddressLine1: "1 Test Street",
        AddressLine2: null,
        City: "Testville",
        PostalCode: "12345",
        Country: "Testland",
        PhoneNumber: "+15551234567",
        CurrencyCode: "USD",
        OrderStatus: "Paid",
        PaymentStatus: "Succeeded",
        CardBrand: "Visa",
        CardLast4: "4242",
        Subtotal: 258m,
        Discount: 60m,
        Shipping: 10m,
        GrandTotal: 208m,
        Lines: [new InvoiceLine("Aurora Wireless Headphones", "Electronics", 2, 129m, 99m, 198m)]);

    [Fact]
    public async Task Generated_pdf_has_valid_signature_and_is_non_empty()
    {
        var bytes = await new InvoicePdfService().GenerateAsync(SampleInvoice());

        Assert.True(bytes.Length > 1000, "Invoice PDF should be a non-trivial document.");
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(1024, bytes.Length));
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public async Task Generation_handles_untrusted_snapshot_text_without_throwing()
    {
        var invoice = SampleInvoice() with
        {
            CustomerName = "<script>alert(1)</script>",
            Lines = [new InvoiceLine("Item & <b>bold</b>", "C&A", 1, 10m, 10m, 10m)]
        };

        var bytes = await new InvoicePdfService().GenerateAsync(invoice);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
