using System.Security.Claims;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Invoices;

public interface IInvoiceService
{
    Task<InvoiceModel?> GetInvoiceAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default);
    Task<InvoiceModel?> GetInvoiceAsync(string userId, string orderNumber, CancellationToken cancellationToken = default);
}

public sealed class InvoiceService(ApplicationDbContext db, IOptions<StoreOptions> storeOptions) : IInvoiceService
{
    private readonly string _storeName = storeOptions.Value.Name;

    public Task<InvoiceModel?> GetInvoiceAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default)
    {
        var userId = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        return userId is null ? Task.FromResult<InvoiceModel?>(null) : GetInvoiceAsync(userId, orderNumber, cancellationToken);
    }

    public async Task<InvoiceModel?> GetInvoiceAsync(string userId, string orderNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(orderNumber))
        {
            return null;
        }

        // Ownership is enforced in the query: both order number AND user id are required before any
        // invoice bytes are produced, so a customer cannot download another customer's invoice.
        var order = await db.Orders.AsNoTracking()
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.PaymentRecord)
            .SingleOrDefaultAsync(candidate => candidate.OrderNumber == orderNumber && candidate.UserId == userId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new InvoiceModel(
            _storeName,
            order.OrderNumber,
            order.InvoiceNumber,
            order.CreatedAtUtc,
            $"{order.CustomerFirstName} {order.CustomerLastName}".Trim(),
            order.CustomerEmail,
            order.ShippingAddressLine1,
            order.ShippingAddressLine2,
            order.ShippingCity,
            order.ShippingPostalCode,
            order.ShippingCountry,
            order.ShippingPhoneNumber,
            order.CurrencyCode,
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.PaymentRecord?.CardBrand,
            order.PaymentRecord?.CardLast4,
            order.SubtotalAmount,
            order.DiscountAmount,
            order.ShippingAmount,
            order.GrandTotal,
            order.Items
                .OrderBy(item => item.ProductName)
                .ThenBy(item => item.Id)
                .Select(item => new InvoiceLine(item.ProductName, item.CategoryName, item.Quantity, item.ListUnitPrice, item.PaidUnitPrice, item.LineTotal))
                .ToArray());
    }
}
