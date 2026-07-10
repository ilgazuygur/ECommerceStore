using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Orders;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Admin;

public interface IAdminDashboardService
{
    Task<DashboardViewModel> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminDashboardService(ApplicationDbContext db, IOptions<StoreOptions> storeOptions) : IAdminDashboardService
{
    private readonly int _lowStockThreshold = storeOptions.Value.LowStockThreshold;

    public async Task<DashboardViewModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var totalProducts = await db.Products.CountAsync(cancellationToken);
        var activeProducts = await db.Products.CountAsync(product => product.IsActive, cancellationToken);

        // Customers are users holding the Customer role (excludes the seeded administrator).
        var customerRoleId = await db.Roles.Where(role => role.Name == RoleNames.Customer).Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        var totalCustomers = customerRoleId is null ? 0 : await db.UserRoles.CountAsync(link => link.RoleId == customerRoleId, cancellationToken);

        var totalOrders = await db.Orders.CountAsync(cancellationToken);
        // The approved dashboard definitions use the literal Pending and Delivered states.
        var pendingOrders = await db.Orders.CountAsync(order => order.OrderStatus == OrderStatus.Pending, cancellationToken);
        var completedOrders = await db.Orders.CountAsync(order => order.OrderStatus == OrderStatus.Delivered, cancellationToken);

        // Revenue counts every successfully paid order (including later-cancelled; refunds are out of scope).
        // GrandTotal is a value-converted decimal (stored as integer minor units), which EF cannot SUM in
        // SQL, so the paid totals are summed exactly in memory as decimals (never floating point).
        var paidTotals = await db.Orders
            .Where(order => order.PaymentStatus == PaymentStatus.Succeeded)
            .Select(order => order.GrandTotal)
            .ToListAsync(cancellationToken);
        var revenue = paidTotals.Sum();

        var lowStock = await db.Products.AsNoTracking()
            .Where(product => product.IsActive && product.StockQuantity > 0 && product.StockQuantity <= _lowStockThreshold)
            .OrderBy(product => product.StockQuantity)
            .ThenBy(product => product.Name)
            .Take(20)
            .Select(product => new LowStockItem(product.Id, product.Name, product.StockQuantity, product.IsActive))
            .ToListAsync(cancellationToken);

        return new DashboardViewModel
        {
            TotalProducts = totalProducts,
            ActiveProducts = activeProducts,
            TotalCustomers = totalCustomers,
            TotalOrders = totalOrders,
            PendingOrders = pendingOrders,
            CompletedOrders = completedOrders,
            Revenue = revenue,
            LowStockThreshold = _lowStockThreshold,
            LowStockProducts = lowStock
        };
    }
}
