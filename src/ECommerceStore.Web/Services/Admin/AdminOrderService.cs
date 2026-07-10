using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Orders;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Admin;

public interface IAdminOrderService
{
    Task<AdminOrderListViewModel> GetListAsync(AdminOrderQuery query, CancellationToken cancellationToken = default);
    Task<AdminOrderDetailsViewModel?> GetDetailsAsync(long orderId, CancellationToken cancellationToken = default);
    Task<AdminResult> UpdateStatusAsync(AdminOrderStatusInput input, CancellationToken cancellationToken = default);
}

public sealed class AdminOrderService(ApplicationDbContext db, IOptions<StoreOptions> storeOptions) : IAdminOrderService
{
    private readonly int _pageSize = storeOptions.Value.AdminPageSize;
    private static readonly string[] StatusNames = Enum.GetNames<OrderStatus>();

    public async Task<AdminOrderListViewModel> GetListAsync(AdminOrderQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var orders = db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            orders = orders.Where(order => EF.Functions.Like(order.OrderNumber, $"%{search}%") || EF.Functions.Like(order.CustomerEmail, $"%{search}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<OrderStatus>(query.Status, out var status))
        {
            orders = orders.Where(order => order.OrderStatus == status);
        }

        var total = await orders.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)_pageSize));
        page = Math.Min(page, totalPages);

        var items = await orders
            .OrderByDescending(order => order.CreatedAtUtc).ThenByDescending(order => order.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize)
            .Select(order => new AdminOrderListItem(
                order.Id, order.OrderNumber, order.CustomerEmail, order.CreatedAtUtc, order.GrandTotal,
                order.OrderStatus.ToString(), order.PaymentStatus.ToString(), order.Items.Count))
            .ToListAsync(cancellationToken);

        return new AdminOrderListViewModel { Query = query, Orders = items, StatusOptions = StatusNames, Page = page, PageSize = _pageSize, TotalCount = total };
    }

    public async Task<AdminOrderDetailsViewModel?> GetDetailsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var addressParts = new[] { order.ShippingAddressLine1, order.ShippingAddressLine2, $"{order.ShippingCity}, {order.ShippingPostalCode}", order.ShippingCountry, order.ShippingPhoneNumber };
        return new AdminOrderDetailsViewModel
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            InvoiceNumber = order.InvoiceNumber,
            CreatedAtUtc = order.CreatedAtUtc,
            CustomerName = $"{order.CustomerFirstName} {order.CustomerLastName}".Trim(),
            CustomerEmail = order.CustomerEmail,
            Address = string.Join(", ", addressParts.Where(part => !string.IsNullOrWhiteSpace(part))),
            OrderStatus = order.OrderStatus.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Subtotal = order.SubtotalAmount,
            Discount = order.DiscountAmount,
            Shipping = order.ShippingAmount,
            GrandTotal = order.GrandTotal,
            Version = order.Version,
            Lines = order.Items.OrderBy(item => item.ProductName).ThenBy(item => item.Id)
                .Select(item => new AdminOrderLine(item.ProductName, item.Quantity, item.PaidUnitPrice, item.LineTotal)).ToArray(),
            AllowedTransitions = OrderStatusPolicy.AllowedTransitions(order.OrderStatus).Select(status => status.ToString()).ToArray()
        };
    }

    public async Task<AdminResult> UpdateStatusAsync(AdminOrderStatusInput input, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<OrderStatus>(input.NewStatus, out var target))
        {
            return AdminResult.Invalid("Unknown order status.");
        }

        var order = await db.Orders.SingleOrDefaultAsync(candidate => candidate.Id == input.OrderId, cancellationToken);
        if (order is null)
        {
            return AdminResult.NotFound();
        }

        if (!OrderStatusPolicy.CanTransition(order.OrderStatus, target, order.PaymentStatus))
        {
            return AdminResult.Invalid($"An order that is {order.OrderStatus} cannot move to {target}.");
        }

        // Only the fulfilment status changes. Payment status and all financial/address/item snapshots are
        // immutable here.
        order.OrderStatus = target;
        db.Entry(order).Property(candidate => candidate.Version).OriginalValue = input.Version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AdminResult.Ok($"Order status updated to {target}.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminResult.Conflict("This order was updated by someone else. Reload and try again.");
        }
    }
}
