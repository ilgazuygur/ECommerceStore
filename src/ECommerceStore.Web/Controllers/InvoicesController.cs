using ECommerceStore.Web.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Controllers;

[Authorize, Route("invoices")]
public sealed class InvoicesController(
    IInvoiceService invoiceService,
    IInvoicePdfService invoicePdf,
    ILogger<InvoicesController> logger) : Controller
{
    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Download(string orderNumber, CancellationToken cancellationToken)
    {
        // Ownership is enforced before any bytes are produced (order number + current user id).
        var invoice = await invoiceService.GetInvoiceAsync(User, orderNumber, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        try
        {
            var bytes = await invoicePdf.GenerateAsync(invoice, cancellationToken);
            return File(bytes, "application/pdf", invoice.FileName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Invoice generation failed for order {OrderNumber}; returning a friendly retry.", orderNumber);
            TempData["Error"] = "We could not generate your invoice just now. Please try again in a moment.";
            return RedirectToAction("Details", "Orders", new { orderNumber });
        }
    }
}
