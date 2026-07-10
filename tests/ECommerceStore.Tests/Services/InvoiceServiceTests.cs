using System.Security.Claims;
using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Invoices;
using ECommerceStore.Web.Services.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Services;

public sealed class InvoiceServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakePaymentRequest SuccessCard = new("4242424242424242", "Test User", 12, 30, "123");

    [Fact]
    public async Task Invoice_is_owner_scoped_and_populated_from_snapshots()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await SeedOrderAsync(context, "owner", cartQty: 2, normalPrice: 25m, discountPrice: 20m);
        await SeedUserAsync(context, "intruder");
        var placed = await BuildCheckout(context).PlaceOrderAsync(Authed("owner"), Command());

        var service = new InvoiceService(context, Options.Create(new StoreOptions { Name = "ECommerceStore" }));

        var invoice = await service.GetInvoiceAsync(Authed("owner"), placed.OrderNumber!);
        Assert.NotNull(invoice);
        Assert.Equal("ECommerceStore", invoice!.StoreName);
        Assert.Equal(placed.OrderNumber, invoice.OrderNumber);
        Assert.Equal("Test User", invoice.CustomerName);
        Assert.Equal("Visa", invoice.CardBrand);
        Assert.Equal("4242", invoice.CardLast4);
        Assert.Equal(50m, invoice.Subtotal);
        Assert.Equal(10m, invoice.Discount);
        Assert.Equal(10m, invoice.Shipping);
        Assert.Equal(50m, invoice.GrandTotal);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal("Widget", line.ProductName);
        Assert.Equal(2, line.Quantity);
        Assert.EndsWith(".pdf", invoice.FileName);

        // A different customer can never obtain another customer's invoice.
        Assert.Null(await service.GetInvoiceAsync(Authed("intruder"), placed.OrderNumber!));
        Assert.Null(await service.GetInvoiceAsync(Authed("owner"), "ORD-missing"));
    }

    private static CheckoutService BuildCheckout(ApplicationDbContext db)
    {
        var clock = new StubClock(Now);
        var calculator = new CartCalculator(Options.Create(new StoreOptions { ShippingFee = 10m }));
        var cartService = new CartService(db, new NoopStore(), calculator, clock);
        return new CheckoutService(db, cartService, new FakePaymentService(clock), new OrderNumberGenerator(clock), clock, NullLogger<CheckoutService>.Instance);
    }

    private static ClaimsPrincipal Authed(string userId) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth"));

    private static PlaceOrderCommand Command() =>
        new(Guid.NewGuid(), "1 Test Street", null, "Testville", "12345", "Testland", "+15551234567", SuccessCard);

    private static async Task SeedOrderAsync(ApplicationDbContext db, string userId, int cartQty, decimal normalPrice, decimal? discountPrice)
    {
        await SeedUserAsync(db, userId);
        var category = new Category { Name = $"Cat {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), Slug = Guid.NewGuid().ToString("N") };
        var product = new Product
        {
            Category = category, Name = "Widget", Slug = Guid.NewGuid().ToString("N"),
            ShortDescription = "Short", FullDescription = "Full", NormalPrice = normalPrice,
            DiscountPrice = discountPrice, StockQuantity = 5, IsActive = true
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
            Id = userId, UserName = $"{userId}@example.test", NormalizedUserName = $"{userId}@EXAMPLE.TEST".ToUpperInvariant(),
            Email = $"{userId}@example.test", NormalizedEmail = $"{userId}@EXAMPLE.TEST".ToUpperInvariant(), FirstName = "Test", LastName = "User"
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
