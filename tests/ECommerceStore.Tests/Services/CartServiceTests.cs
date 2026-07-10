using System.Security.Claims;
using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Cart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Services;

public sealed class CartServiceTests
{
    [Fact]
    public void Calculator_uses_line_rounding_discount_and_nonempty_shipping()
    {
        var calculator = new CartCalculator(Options.Create(new StoreOptions { ShippingFee = 10m }));

        var totals = calculator.Calculate([
            new CartPriceLine(1, 3, 19.99m, 14.49m),
            new CartPriceLine(2, 2, 5.25m, 5.25m)
        ]);

        Assert.Equal(70.47m, totals.ListPriceSubtotal);
        Assert.Equal(16.50m, totals.DiscountTotal);
        Assert.Equal(53.97m, totals.MerchandiseTotal);
        Assert.Equal(10m, totals.Shipping);
        Assert.Equal(63.97m, totals.GrandTotal);
    }

    [Fact]
    public void Calculator_empty_cart_has_no_shipping()
    {
        var calculator = new CartCalculator(Options.Create(new StoreOptions { ShippingFee = 10m }));
        Assert.Equal(new CartTotals(0, 0, 0, 0, 0), calculator.Calculate([]));
    }

    [Fact]
    public async Task Anonymous_add_caps_quantity_and_recalculates_from_database_prices()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var product = await AddProductAsync(context, stock: 3, normalPrice: 25m, discountPrice: 20m);
        var store = new FakeAnonymousCartStore();
        var service = new CartService(context, store,
            new CartCalculator(Options.Create(new StoreOptions { ShippingFee = 10m })), new SystemClock());

        var result = await service.AddAsync(new ClaimsPrincipal(new ClaimsIdentity()), product.Id, 99);
        var cart = await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True(result.Succeeded);
        Assert.Equal(3, cart.TotalUnits);
        Assert.Equal(60m, cart.Totals.MerchandiseTotal);
        Assert.Equal(70m, cart.Totals.GrandTotal);
        Assert.Equal(new AnonymousCartLine(product.Id, 3), Assert.Single(store.Lines));
    }

    [Fact]
    public async Task Merge_sums_caps_commits_then_clears_anonymous_cart()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var user = new ApplicationUser
        {
            Id = "customer-1", UserName = "customer@example.test", NormalizedUserName = "CUSTOMER@EXAMPLE.TEST",
            Email = "customer@example.test", NormalizedEmail = "CUSTOMER@EXAMPLE.TEST", FirstName = "Test", LastName = "Customer"
        };
        context.Users.Add(user);
        var product = await AddProductAsync(context, stock: 5, normalPrice: 25m);
        var cart = new ShoppingCart { UserId = user.Id };
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 3 });
        await context.SaveChangesAsync();
        var store = new FakeAnonymousCartStore([new AnonymousCartLine(product.Id, 4)]);

        var result = await new CartMergeService(context, store, new SystemClock()).MergeAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(store.Lines);
        Assert.Contains(result.Warnings, warning => warning.Contains("capped", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, await context.CartItems.Where(item => item.CartId == cart.Id).Select(item => item.Quantity).SingleAsync());
    }

    private static async Task<Product> AddProductAsync(
        ECommerceStore.Web.Data.ApplicationDbContext context,
        int stock,
        decimal normalPrice,
        decimal? discountPrice = null)
    {
        var category = new Category { Name = $"Category {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), Slug = Guid.NewGuid().ToString("N") };
        var product = new Product
        {
            Category = category, Name = "Test Product", Slug = Guid.NewGuid().ToString("N"),
            ShortDescription = "Short", FullDescription = "Full", NormalPrice = normalPrice,
            DiscountPrice = discountPrice, StockQuantity = stock, IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private sealed class FakeAnonymousCartStore(IEnumerable<AnonymousCartLine>? initial = null) : IAnonymousCartStore
    {
        public List<AnonymousCartLine> Lines { get; } = initial?.ToList() ?? [];
        public IReadOnlyList<AnonymousCartLine> Read() => Lines.ToArray();
        public void Write(IEnumerable<AnonymousCartLine> lines) { Lines.Clear(); Lines.AddRange(lines); }
        public void Clear() => Lines.Clear();
    }
}
