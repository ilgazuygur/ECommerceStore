using System.Security.Claims;
using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Orders;
using ECommerceStore.Web.Services.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Services;

public sealed class OrderQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakePaymentRequest SuccessCard = new("4242424242424242", "Test User", 12, 30, "123");

    [Fact]
    public async Task My_orders_lists_only_the_current_users_orders()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var checkout = BuildCheckout(context);
        await SeedUserWithCartAsync(context, "owner", stock: 5, cartQty: 1);
        await SeedUserWithCartAsync(context, "other", stock: 5, cartQty: 1);
        await checkout.PlaceOrderAsync(Authed("owner"), Command(SuccessCard));
        await checkout.PlaceOrderAsync(Authed("other"), Command(SuccessCard));

        var query = new OrderQueryService(context);
        var ownerList = await query.GetMyOrdersAsync(Authed("owner"), page: 1);

        Assert.Equal(1, ownerList.TotalCount);
        var listed = Assert.Single(ownerList.Orders);
        Assert.Equal("Succeeded", listed.PaymentStatus);
        Assert.Equal("Paid", listed.OrderStatus);
        Assert.Equal(1, listed.ItemCount);
    }

    [Fact]
    public async Task Order_details_are_owner_scoped_and_resist_idor()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var checkout = BuildCheckout(context);
        await SeedUserWithCartAsync(context, "owner", stock: 5, cartQty: 2);
        await SeedUserAsync(context, "intruder");
        var placed = await checkout.PlaceOrderAsync(Authed("owner"), Command(SuccessCard));

        var query = new OrderQueryService(context);
        var owned = await query.GetOrderDetailsAsync(Authed("owner"), placed.OrderNumber!);
        Assert.NotNull(owned);
        Assert.Equal(2, Assert.Single(owned!.Lines).Quantity);

        // A different customer cannot open the order by guessing/altering the route value.
        Assert.Null(await query.GetOrderDetailsAsync(Authed("intruder"), placed.OrderNumber!));
        Assert.Null(await query.GetOrderDetailsAsync(Authed("owner"), "ORD-nope"));
    }

    private static CheckoutService BuildCheckout(ApplicationDbContext db)
    {
        var clock = new StubClock(Now);
        var calculator = new CartCalculator(Options.Create(new StoreOptions { ShippingFee = 10m }));
        var cartService = new CartService(db, new NoopStore(), calculator, clock);
        return new CheckoutService(db, cartService, new FakePaymentService(clock), new OrderNumberGenerator(clock), clock, NullLogger<CheckoutService>.Instance);
    }

    private static ClaimsPrincipal Authed(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth"));

    private static PlaceOrderCommand Command(FakePaymentRequest payment) =>
        new(Guid.NewGuid(), "1 Test Street", null, "Testville", "12345", "Testland", "+15551234567", payment);

    private static async Task SeedUserWithCartAsync(ApplicationDbContext db, string userId, int stock, int cartQty)
    {
        await SeedUserAsync(db, userId);
        var category = new Category { Name = $"Cat {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), Slug = Guid.NewGuid().ToString("N") };
        var product = new Product
        {
            Category = category, Name = "Widget", Slug = Guid.NewGuid().ToString("N"),
            ShortDescription = "Short", FullDescription = "Full", NormalPrice = 25m,
            StockQuantity = stock, IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var cart = new ShoppingCart { UserId = userId };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();
        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = cartQty });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(ApplicationDbContext db, string userId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"{userId}@example.test",
            NormalizedUserName = $"{userId}@EXAMPLE.TEST".ToUpperInvariant(),
            Email = $"{userId}@example.test",
            NormalizedEmail = $"{userId}@EXAMPLE.TEST".ToUpperInvariant(),
            FirstName = "Test",
            LastName = "User"
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class NoopStore : IAnonymousCartStore
    {
        public IReadOnlyList<AnonymousCartLine> Read() => [];
        public void Write(IEnumerable<AnonymousCartLine> lines) { }
        public void Clear() { }
    }
}
