using ECommerceStore.Web.Data;
using ECommerceStore.Web.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ECommerceStore.Web.Services.Orders;

public interface IInventoryService
{
    Task<bool> TryDecreaseStockAsync(int productId, int quantity, CancellationToken cancellationToken = default);
}

public sealed class InventoryService(ApplicationDbContext db, IClock clock) : IInventoryService
{
    public async Task<bool> TryDecreaseStockAsync(int productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0 || quantity > 1_000_000)
        {
            return false;
        }

        var now = clock.UtcNow;
        var affected = await db.Products
            .Where(product => product.Id == productId && product.IsActive && product.StockQuantity >= quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.StockQuantity, product => product.StockQuantity - quantity)
                .SetProperty(product => product.Version, product => product.Version + 1)
                .SetProperty(product => product.UpdatedAtUtc, now), cancellationToken);

        return affected == 1;
    }
}
