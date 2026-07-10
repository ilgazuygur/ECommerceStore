namespace ECommerceStore.Web.Services.Invoices;

public sealed record InvoiceLine(string ProductName, string CategoryName, int Quantity, decimal ListUnitPrice, decimal PaidUnitPrice, decimal LineTotal);

/// <summary>
/// A fully self-contained invoice projection built from immutable order snapshots. It carries everything
/// the PDF and the order email need, so both render deterministically without touching the live catalog.
/// </summary>
public sealed record InvoiceModel(
    string StoreName,
    string OrderNumber,
    string InvoiceNumber,
    DateTime CreatedAtUtc,
    string CustomerName,
    string CustomerEmail,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string PostalCode,
    string Country,
    string PhoneNumber,
    string CurrencyCode,
    string OrderStatus,
    string PaymentStatus,
    string? CardBrand,
    string? CardLast4,
    decimal Subtotal,
    decimal Discount,
    decimal Shipping,
    decimal GrandTotal,
    IReadOnlyList<InvoiceLine> Lines)
{
    public string FileName
    {
        get
        {
            var safe = new string(InvoiceNumber.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
            return $"Invoice-{(string.IsNullOrWhiteSpace(safe) ? "order" : safe)}.pdf";
        }
    }
}
