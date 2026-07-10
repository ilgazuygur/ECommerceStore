using System.Security.Claims;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.ViewModels.Orders;
using Microsoft.EntityFrameworkCore;

namespace ECommerceStore.Web.Services.Orders;

public interface IOrderQueryService
{
    Task<OrderListViewModel> GetMyOrdersAsync(ClaimsPrincipal user, int page, CancellationToken cancellationToken = default);
    Task<OrderDetailsViewModel?> GetOrderDetailsAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default);
}

public sealed class OrderQueryService(ApplicationDbContext db) : IOrderQueryService
{
    public const int PageSize = 10;

    public async Task<OrderListViewModel> GetMyOrdersAsync(ClaimsPrincipal user, int page, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        if (userId is null)
        {
            return new OrderListViewModel { PageSize = PageSize };
        }

        var currentPage = page < 1 ? 1 : page;
        var query = db.Orders.AsNoTracking().Where(order => order.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        currentPage = Math.Min(currentPage, totalPages);

        var orders = await query
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(order => new OrderListItem
            {
                OrderNumber = order.OrderNumber,
                InvoiceNumber = order.InvoiceNumber,
                CreatedAtUtc = order.CreatedAtUtc,
                GrandTotal = order.GrandTotal,
                OrderStatus = order.OrderStatus.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                ItemCount = order.Items.Count
            })
            .ToListAsync(cancellationToken);

        return new OrderListViewModel
        {
            Orders = orders,
            Page = currentPage,
            PageSize = PageSize,
            TotalCount = total
        };
    }

    public async Task<OrderDetailsViewModel?> GetOrderDetailsAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        if (userId is null || string.IsNullOrWhiteSpace(orderNumber))
        {
            return null;
        }

        // Ownership is enforced in the query: both order number AND current user id are required, so a
        // customer can never open another customer's order by changing the route value.
        var order = await db.Orders.AsNoTracking()
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.PaymentRecord)
            .SingleOrDefaultAsync(candidate => candidate.OrderNumber == orderNumber && candidate.UserId == userId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new OrderDetailsViewModel
        {
            OrderNumber = order.OrderNumber,
            InvoiceNumber = order.InvoiceNumber,
            CreatedAtUtc = order.CreatedAtUtc,
            OrderStatus = order.OrderStatus.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            CustomerName = $"{order.CustomerFirstName} {order.CustomerLastName}".Trim(),
            CustomerEmail = order.CustomerEmail,
            AddressLine1 = order.ShippingAddressLine1,
            AddressLine2 = order.ShippingAddressLine2,
            City = order.ShippingCity,
            PostalCode = order.ShippingPostalCode,
            Country = order.ShippingCountry,
            PhoneNumber = order.ShippingPhoneNumber,
            CardBrand = order.PaymentRecord?.CardBrand,
            CardLast4 = order.PaymentRecord?.CardLast4,
            Subtotal = order.SubtotalAmount,
            Discount = order.DiscountAmount,
            Shipping = order.ShippingAmount,
            GrandTotal = order.GrandTotal,
            Lines = order.Items
                .OrderBy(item => item.ProductName)
                .ThenBy(item => item.Id)
                .Select(item => new OrderDetailLine
                {
                    ProductName = item.ProductName,
                    CategoryName = item.CategoryName,
                    Quantity = item.Quantity,
                    ListUnitPrice = item.ListUnitPrice,
                    PaidUnitPrice = item.PaidUnitPrice,
                    LineDiscount = item.DiscountAmount,
                    LineTotal = item.LineTotal
                })
                .ToArray()
        };
    }

    private static string? GetUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;
}
