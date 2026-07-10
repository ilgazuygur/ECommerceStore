using System.Security.Claims;
using System.Text.Json;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Cart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Cart;

public interface ICartCalculator
{
    CartTotals Calculate(IEnumerable<CartPriceLine> lines);
}

public sealed class CartCalculator(IOptions<StoreOptions> options) : ICartCalculator
{
    private readonly decimal _shippingFee = options.Value.ShippingFee;

    public CartTotals Calculate(IEnumerable<CartPriceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        decimal listSubtotal = 0;
        decimal merchandise = 0;
        var hasLines = false;

        foreach (var line in lines)
        {
            if (line.Quantity is < 1 or > 1_000_000 || line.ListUnitPrice < 0 ||
                line.PaidUnitPrice < 0 || line.PaidUnitPrice > line.ListUnitPrice)
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Cart line values are outside supported bounds.");
            }

            hasLines = true;
            listSubtotal = checked(listSubtotal + Money.Round(line.ListUnitPrice * line.Quantity));
            merchandise = checked(merchandise + Money.Round(line.PaidUnitPrice * line.Quantity));
        }

        listSubtotal = Money.Round(listSubtotal);
        merchandise = Money.Round(merchandise);
        var discount = Money.Round(listSubtotal - merchandise);
        var shipping = hasLines ? Money.Round(_shippingFee) : 0m;
        return new(listSubtotal, discount, merchandise, shipping, Money.Round(merchandise + shipping));
    }
}

public sealed record AnonymousCartLine(int ProductId, int Quantity);
public sealed record AnonymousCartEnvelope(int Version, IReadOnlyList<AnonymousCartLine> Items);

public interface IAnonymousCartStore
{
    IReadOnlyList<AnonymousCartLine> Read();
    void Write(IEnumerable<AnonymousCartLine> lines);
    void Clear();
}

public sealed class AnonymousCartStore(IHttpContextAccessor accessor, ILogger<AnonymousCartStore> logger) : IAnonymousCartStore
{
    private const string SessionKey = "cart.v1";
    private const int CurrentVersion = 1;
    private const int MaximumLines = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ISession Session => accessor.HttpContext?.Session
        ?? throw new InvalidOperationException("An active HTTP session is required.");

    public IReadOnlyList<AnonymousCartLine> Read()
    {
        var json = Session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var envelope = JsonSerializer.Deserialize<AnonymousCartEnvelope>(json, JsonOptions);
            if (envelope is null || envelope.Version != CurrentVersion || envelope.Items.Count > MaximumLines ||
                envelope.Items.Any(item => item.ProductId <= 0 || item.Quantity is < 1 or > 1_000_000) ||
                envelope.Items.Select(item => item.ProductId).Distinct().Count() != envelope.Items.Count)
            {
                Repair();
                return [];
            }

            return envelope.Items;
        }
        catch (JsonException)
        {
            Repair();
            return [];
        }
    }

    public void Write(IEnumerable<AnonymousCartLine> lines)
    {
        var normalized = lines
            .Where(line => line.ProductId > 0 && line.Quantity is >= 1 and <= 1_000_000)
            .GroupBy(line => line.ProductId)
            .Select(group => new AnonymousCartLine(group.Key, (int)Math.Min(group.Sum(line => (long)line.Quantity), 1_000_000)))
            .Take(MaximumLines)
            .OrderBy(line => line.ProductId)
            .ToArray();

        if (normalized.Length == 0)
        {
            Clear();
            return;
        }

        Session.SetString(SessionKey, JsonSerializer.Serialize(new AnonymousCartEnvelope(CurrentVersion, normalized), JsonOptions));
    }

    public void Clear() => Session.Remove(SessionKey);

    private void Repair()
    {
        Session.Remove(SessionKey);
        logger.LogWarning("Discarded an invalid anonymous cart session payload.");
    }
}

public interface ICartService
{
    Task<CartViewModel> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<CartOperationResult> AddAsync(ClaimsPrincipal user, int productId, int quantity, CancellationToken cancellationToken = default);
    Task<CartOperationResult> SetQuantityAsync(ClaimsPrincipal user, int productId, int quantity, CancellationToken cancellationToken = default);
    Task<CartOperationResult> AdjustQuantityAsync(ClaimsPrincipal user, int productId, int delta, CancellationToken cancellationToken = default);
    Task<CartOperationResult> RemoveAsync(ClaimsPrincipal user, int productId, CancellationToken cancellationToken = default);
    Task ClearAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public sealed class CartService(
    ApplicationDbContext db,
    IAnonymousCartStore anonymousStore,
    ICartCalculator calculator,
    IClock clock) : ICartService
{
    public async Task<CartViewModel> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        var quantities = userId is null
            ? anonymousStore.Read().ToDictionary(line => line.ProductId, line => line.Quantity)
            : await LoadDatabaseQuantitiesAsync(userId, cancellationToken);

        if (quantities.Count == 0) return Empty(userId is not null);

        var productIds = quantities.Keys.ToArray();
        var products = await db.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.Id) && product.IsActive && product.Category.IsActive && product.StockQuantity > 0)
            .Select(product => new
            {
                product.Id, product.Name, product.Slug, CategoryName = product.Category.Name,
                product.ImageKind, product.ImageLocation, product.NormalPrice, product.DiscountPrice, product.StockQuantity
            })
            .ToListAsync(cancellationToken);

        var lines = products.Select(product =>
        {
            var quantity = Math.Min(quantities[product.Id], product.StockQuantity);
            var paid = product.DiscountPrice ?? product.NormalPrice;
            return new CartLineViewModel
            {
                ProductId = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                CategoryName = product.CategoryName,
                ImageUrl = ResolveImageUrl(product.ImageKind, product.ImageLocation),
                Quantity = quantity,
                MaximumQuantity = product.StockQuantity,
                ListUnitPrice = product.NormalPrice,
                PaidUnitPrice = paid,
                LineTotal = Money.Round(paid * quantity),
                LineDiscount = Money.Round((product.NormalPrice - paid) * quantity)
            };
        }).OrderBy(line => line.Name).ThenBy(line => line.ProductId).ToArray();

        await RepairUnavailableAsync(userId, quantities, lines, cancellationToken);
        var totals = calculator.Calculate(lines.Select(line => new CartPriceLine(line.ProductId, line.Quantity, line.ListUnitPrice, line.PaidUnitPrice)));
        return new CartViewModel
        {
            Lines = lines,
            Totals = totals,
            TotalUnits = lines.Sum(line => line.Quantity),
            IsAuthenticated = userId is not null
        };
    }

    public async Task<int> GetCountAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        (await GetAsync(user, cancellationToken)).TotalUnits;

    public async Task<CartOperationResult> AddAsync(ClaimsPrincipal user, int productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (productId <= 0 || quantity is < 1 or > 1_000_000)
            return CartOperationResult.Failure("Choose a valid quantity.");

        var product = await LoadAvailableProductAsync(productId, cancellationToken);
        if (product is null) return CartOperationResult.Failure("That product is unavailable.");

        var userId = GetUserId(user);
        if (userId is null)
        {
            var lines = anonymousStore.Read().ToDictionary(line => line.ProductId, line => line.Quantity);
            var existing = lines.GetValueOrDefault(productId);
            lines[productId] = (int)Math.Min(checked((long)existing + quantity), product.StockQuantity);
            anonymousStore.Write(lines.Select(line => new AnonymousCartLine(line.Key, line.Value)));
        }
        else
        {
            var cart = await GetOrCreateCartAsync(userId, cancellationToken);
            var item = await db.CartItems.SingleOrDefaultAsync(line => line.CartId == cart.Id && line.ProductId == productId, cancellationToken);
            if (item is null)
                db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = productId, Quantity = Math.Min(quantity, product.StockQuantity) });
            else
                item.Quantity = (int)Math.Min(checked((long)item.Quantity + quantity), product.StockQuantity);
            cart.UpdatedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return CartOperationResult.Success(quantity > product.StockQuantity ? "Quantity was capped to available stock." : "Added to your cart.");
    }

    public async Task<CartOperationResult> SetQuantityAsync(ClaimsPrincipal user, int productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (productId <= 0 || quantity is < 1 or > 1_000_000)
            return CartOperationResult.Failure("Choose a valid quantity.");

        var product = await LoadAvailableProductAsync(productId, cancellationToken);
        if (product is null) return CartOperationResult.Failure("That product is unavailable.");
        var corrected = Math.Min(quantity, product.StockQuantity);
        var userId = GetUserId(user);

        if (userId is null)
        {
            var lines = anonymousStore.Read().ToDictionary(line => line.ProductId, line => line.Quantity);
            if (!lines.ContainsKey(productId)) return CartOperationResult.Failure("That item is not in your cart.");
            lines[productId] = corrected;
            anonymousStore.Write(lines.Select(line => new AnonymousCartLine(line.Key, line.Value)));
        }
        else
        {
            var item = await db.CartItems.SingleOrDefaultAsync(line => line.Cart.UserId == userId && line.ProductId == productId, cancellationToken);
            if (item is null) return CartOperationResult.Failure("That item is not in your cart.");
            item.Quantity = corrected;
            await db.SaveChangesAsync(cancellationToken);
        }

        return CartOperationResult.Success(corrected == quantity ? "Cart updated." : "Quantity was capped to available stock.");
    }

    public async Task<CartOperationResult> AdjustQuantityAsync(ClaimsPrincipal user, int productId, int delta, CancellationToken cancellationToken = default)
    {
        if (productId <= 0 || delta is not (-1 or 1)) return CartOperationResult.Failure("Invalid cart change.");
        var userId = GetUserId(user);
        var current = userId is null
            ? anonymousStore.Read().SingleOrDefault(line => line.ProductId == productId)?.Quantity
            : await db.CartItems.AsNoTracking().Where(line => line.Cart.UserId == userId && line.ProductId == productId)
                .Select(line => (int?)line.Quantity).SingleOrDefaultAsync(cancellationToken);
        if (!current.HasValue) return CartOperationResult.Failure("That item is not in your cart.");
        if (current.Value == 1 && delta == -1) return await RemoveAsync(user, productId, cancellationToken);
        return await SetQuantityAsync(user, productId, current.Value + delta, cancellationToken);
    }

    public async Task<CartOperationResult> RemoveAsync(ClaimsPrincipal user, int productId, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) return CartOperationResult.Failure("Invalid cart item.");
        var userId = GetUserId(user);
        if (userId is null)
        {
            var lines = anonymousStore.Read().Where(line => line.ProductId != productId).ToArray();
            anonymousStore.Write(lines);
        }
        else
        {
            var item = await db.CartItems.SingleOrDefaultAsync(line => line.Cart.UserId == userId && line.ProductId == productId, cancellationToken);
            if (item is not null)
            {
                db.CartItems.Remove(item);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return CartOperationResult.Success("Item removed.");
    }

    public async Task ClearAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        if (userId is null) anonymousStore.Clear();
        else await db.CartItems.Where(item => item.Cart.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<Dictionary<int, int>> LoadDatabaseQuantitiesAsync(string userId, CancellationToken cancellationToken) =>
        await db.CartItems.AsNoTracking().Where(item => item.Cart.UserId == userId)
            .ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);

    private async Task<Product?> LoadAvailableProductAsync(int productId, CancellationToken cancellationToken) =>
        await db.Products.Include(product => product.Category)
            .SingleOrDefaultAsync(product => product.Id == productId && product.IsActive && product.Category.IsActive && product.StockQuantity > 0, cancellationToken);

    private async Task<ShoppingCart> GetOrCreateCartAsync(string userId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (cart is not null) return cart;
        cart = new ShoppingCart { UserId = userId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(cancellationToken);
        return cart;
    }

    private async Task RepairUnavailableAsync(string? userId, IReadOnlyDictionary<int, int> original, IReadOnlyList<CartLineViewModel> valid, CancellationToken cancellationToken)
    {
        var repaired = valid.ToDictionary(line => line.ProductId, line => line.Quantity);
        if (original.Count == repaired.Count && original.All(line => repaired.GetValueOrDefault(line.Key) == line.Value)) return;
        if (userId is null)
        {
            anonymousStore.Write(repaired.Select(line => new AnonymousCartLine(line.Key, line.Value)));
            return;
        }

        var validIds = repaired.Keys.ToArray();
        await db.CartItems.Where(item => item.Cart.UserId == userId && !validIds.Contains(item.ProductId)).ExecuteDeleteAsync(cancellationToken);
        var changed = await db.CartItems.Where(item => item.Cart.UserId == userId).ToListAsync(cancellationToken);
        foreach (var item in changed)
            if (repaired.TryGetValue(item.ProductId, out var quantity)) item.Quantity = quantity;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? GetUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    private static string? ResolveImageUrl(ProductImageKind kind, string? location) => kind switch
    {
        ProductImageKind.Local when !string.IsNullOrWhiteSpace(location) => "/" + location.TrimStart('/'),
        ProductImageKind.ExternalUrl when Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps => uri.AbsoluteUri,
        _ => null
    };

    private static CartViewModel Empty(bool authenticated) => new() { IsAuthenticated = authenticated };
}

public interface ICartMergeService
{
    Task<CartMergeResult> MergeAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class CartMergeService(ApplicationDbContext db, IAnonymousCartStore anonymousStore, IClock clock) : ICartMergeService
{
    public async Task<CartMergeResult> MergeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var anonymous = anonymousStore.Read();
        if (anonymous.Count == 0) return CartMergeResult.Empty;
        var warnings = new List<string>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var cart = await db.Carts.Include(item => item.Items).SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            if (cart is null)
            {
                cart = new ShoppingCart { UserId = userId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
                db.Carts.Add(cart);
                await db.SaveChangesAsync(cancellationToken);
            }

            var ids = anonymous.Select(item => item.ProductId).ToArray();
            var products = await db.Products.Where(product => ids.Contains(product.Id) && product.IsActive && product.Category.IsActive && product.StockQuantity > 0)
                .ToDictionaryAsync(product => product.Id, cancellationToken);
            foreach (var incoming in anonymous)
            {
                if (!products.TryGetValue(incoming.ProductId, out var product))
                {
                    warnings.Add("An unavailable item was removed during cart merge.");
                    continue;
                }

                var existing = cart.Items.SingleOrDefault(item => item.ProductId == incoming.ProductId);
                var combined = checked((long)(existing?.Quantity ?? 0) + incoming.Quantity);
                var quantity = (int)Math.Min(Math.Min(combined, product.StockQuantity), 1_000_000);
                if (quantity < combined) warnings.Add($"{product.Name} was capped to available stock.");
                if (existing is null) cart.Items.Add(new CartItem { ProductId = product.Id, Quantity = quantity });
                else existing.Quantity = quantity;
            }

            cart.UpdatedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            anonymousStore.Clear();
            return new(true, warnings.Distinct().ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(false, ["Your account is ready, but the anonymous cart could not be merged. It remains available for a retry."]);
        }
    }
}
