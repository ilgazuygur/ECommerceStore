using ECommerceStore.Web.Services.Email;
using ECommerceStore.Web.Services.Invoices;

namespace ECommerceStore.Web.Services.Orders;

/// <summary>
/// Runs post-commit checkout side effects. Invoice generation and order email are best-effort: a failure
/// is logged with only the order number (never address or payment input) and never rolls back the order.
/// </summary>
public interface IOrderConfirmationDispatcher
{
    Task DispatchAsync(string userId, string orderNumber, CancellationToken cancellationToken = default);
}

public sealed class OrderConfirmationDispatcher(
    IInvoiceService invoiceService,
    IInvoicePdfService invoicePdf,
    IEmailComposer emailComposer,
    IEmailService emailService,
    ILogger<OrderConfirmationDispatcher> logger) : IOrderConfirmationDispatcher
{
    public async Task DispatchAsync(string userId, string orderNumber, CancellationToken cancellationToken = default)
    {
        InvoiceModel? invoice;
        try
        {
            invoice = await invoiceService.GetInvoiceAsync(userId, orderNumber, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Could not load the invoice model for order {OrderNumber} during post-commit dispatch.", orderNumber);
            return;
        }

        if (invoice is null)
        {
            return;
        }

        // Attempt generation now for early failure detection; authorized downloads regenerate on demand.
        try
        {
            _ = await invoicePdf.GenerateAsync(invoice, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Invoice PDF generation failed for order {OrderNumber}; the order remains valid and download will retry.", orderNumber);
        }

        try
        {
            var message = emailComposer.ComposeOrderConfirmation(invoice);
            var result = await emailService.SendAsync(message, cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogWarning("Order email for {OrderNumber} was not delivered ({Code}); the order remains valid.", orderNumber, result.Code);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Order email composition/send failed for order {OrderNumber}; the order remains valid.", orderNumber);
        }
    }
}
