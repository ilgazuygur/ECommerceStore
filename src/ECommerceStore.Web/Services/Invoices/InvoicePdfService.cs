using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ECommerceStore.Web.Services.Invoices;

public interface IInvoicePdfService
{
    Task<byte[]> GenerateAsync(InvoiceModel invoice, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders a professional single-document invoice from an <see cref="InvoiceModel"/> in memory. Invoices
/// are never stored on disk — they are regenerated deterministically from order snapshots on demand.
/// </summary>
public sealed class InvoicePdfService : IInvoicePdfService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    static InvoicePdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<byte[]> GenerateAsync(InvoiceModel invoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Compose(invoice).GeneratePdf(), cancellationToken);
    }

    private static Document Compose(InvoiceModel invoice) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Column(header =>
            {
                header.Item().Text(invoice.StoreName).FontSize(20).SemiBold().FontColor(Colors.Green.Darken3);
                header.Item().Text("Invoice").FontSize(14).SemiBold();
            });

            page.Content().PaddingVertical(15).Column(content =>
            {
                content.Spacing(14);

                content.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Billed to").SemiBold();
                        left.Item().Text(invoice.CustomerName);
                        left.Item().Text(invoice.CustomerEmail);
                        left.Item().Text(invoice.AddressLine1);
                        if (!string.IsNullOrWhiteSpace(invoice.AddressLine2))
                        {
                            left.Item().Text(invoice.AddressLine2);
                        }

                        left.Item().Text($"{invoice.City}, {invoice.PostalCode}");
                        left.Item().Text(invoice.Country);
                        left.Item().Text(invoice.PhoneNumber);
                    });

                    row.RelativeItem().AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text($"Invoice: {invoice.InvoiceNumber}").SemiBold();
                        right.Item().AlignRight().Text($"Order: {invoice.OrderNumber}");
                        right.Item().AlignRight().Text($"Date: {invoice.CreatedAtUtc.ToString("dd MMM yyyy HH:mm", Culture)} UTC");
                        right.Item().AlignRight().Text($"Order status: {invoice.OrderStatus}");
                        right.Item().AlignRight().Text($"Payment: {invoice.PaymentStatus}");
                    });
                });

                content.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                    });

                    table.Header(head =>
                    {
                        head.Cell().Element(HeaderCell).Text("Item");
                        head.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                        head.Cell().Element(HeaderCell).AlignRight().Text("Unit price");
                        head.Cell().Element(HeaderCell).AlignRight().Text("Line total");
                    });

                    foreach (var line in invoice.Lines)
                    {
                        table.Cell().Element(BodyCell).Column(cell =>
                        {
                            cell.Item().Text(line.ProductName);
                            cell.Item().Text(line.CategoryName).FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                        table.Cell().Element(BodyCell).AlignRight().Text(line.Quantity.ToString(Culture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Money(line.PaidUnitPrice));
                        table.Cell().Element(BodyCell).AlignRight().Text(Money(line.LineTotal));
                    }
                });

                content.Item().AlignRight().Column(totals =>
                {
                    totals.Item().Text($"List subtotal: {Money(invoice.Subtotal)}");
                    totals.Item().Text($"Discount: -{Money(invoice.Discount)}");
                    totals.Item().Text($"Shipping: {Money(invoice.Shipping)}");
                    totals.Item().PaddingTop(4).Text($"Grand total: {Money(invoice.GrandTotal)}").FontSize(13).SemiBold();
                });

                if (invoice.CardLast4 is not null)
                {
                    content.Item().Text($"Paid with {invoice.CardBrand} ending {invoice.CardLast4} via the demo gateway.").FontSize(9).FontColor(Colors.Grey.Medium);
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"{invoice.StoreName} · Prices are tax-inclusive · ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span("This is a development demo. No real payment was taken.").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    });

    private static string Money(decimal value) => value.ToString("C", Culture);

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(4).DefaultTextStyle(style => style.SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
}
