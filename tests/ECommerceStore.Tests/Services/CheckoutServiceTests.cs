using System.Security.Claims;
using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Models.Orders;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Services;

public sealed class CheckoutServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakePaymentRequest SuccessCard = new("4242424242424242", "Test User", 12, 30, "123");
    private static readonly FakePaymentRequest DeclineCard = new("4000000000000002", "Test User", 12, 30, "123");

    [Fact]
    public async Task Successful_checkout_creates_balanced_order_reduces_stock_and_clears_cart()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var product = await SeedUserWithCartAsync(context, "user-1", stock: 5, normalPrice: 25m, discountPrice: 20m, cartQty: 2);
        var service = BuildService(context);

        var result = await service.PlaceOrderAsync(Authed("user-1"), Command(Guid.NewGuid(), SuccessCard));

        Assert.Equal(CheckoutOutcome.Success, result.Outcome);

        var order = await context.Orders.Include(o => o.Items).Include(o => o.PaymentRecord).SingleAsync();
        Assert.Equal(result.OrderNumber, order.OrderNumber);
        Assert.Equal(OrderStatus.Paid, order.OrderStatus);
        Assert.Equal(PaymentStatus.Succeeded, order.PaymentStatus);
        // Balanced: subtotal - discount + shipping == grand total.
        Assert.Equal(order.GrandTotal, order.SubtotalAmount - order.DiscountAmount + order.ShippingAmount);
        Assert.Equal(50m, order.SubtotalAmount);    // 2 x 25 list price
        Assert.Equal(10m, order.DiscountAmount);    // 2 x (25-20)
        Assert.Equal(10m, order.ShippingAmount);
        Assert.Equal(50m, order.GrandTotal);        // 40 merchandise + 10 shipping

        var item = Assert.Single(order.Items);
        Assert.Equal("Widget", item.ProductName);   // snapshot
        Assert.Equal(2, item.Quantity);
        Assert.Equal(20m, item.PaidUnitPrice);
        Assert.Equal(40m, item.LineTotal);

        Assert.Equal(3, await context.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        Assert.Empty(await context.CartItems.Where(c => c.Cart.UserId == "user-1").ToListAsync());
        Assert.Equal(PaymentStatus.Succeeded, order.PaymentRecord!.Status);
        Assert.Equal("4242", order.PaymentRecord.CardLast4);

        var attempt = await context.CheckoutAttempts.SingleAsync();
        Assert.Equal(CheckoutAttemptStatus.Succeeded, attempt.Status);
    }

    [Fact]
    public async Task Declined_card_creates_no_order_and_preserves_stock_and_cart()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var product = await SeedUserWithCartAsync(context, "user-1", stock: 5, normalPrice: 25m, discountPrice: null, cartQty: 2);
        var service = BuildService(context);

        var result = await service.PlaceOrderAsync(Authed("user-1"), Command(Guid.NewGuid(), DeclineCard));

        Assert.Equal(CheckoutOutcome.Declined, result.Outcome);
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Equal(5, await context.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(2, await context.CartItems.Where(c => c.Cart.UserId == "user-1").Select(c => c.Quantity).SingleAsync());

        var payment = await context.PaymentRecords.SingleAsync();
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(payment.OrderId);
        var attempt = await context.CheckoutAttempts.SingleAsync();
        Assert.Equal(CheckoutAttemptStatus.Failed, attempt.Status);
    }

    [Fact]
    public async Task Repeated_token_is_idempotent_and_never_creates_a_second_order()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var product = await SeedUserWithCartAsync(context, "user-1", stock: 5, normalPrice: 25m, discountPrice: null, cartQty: 2);
        var service = BuildService(context);
        var token = Guid.NewGuid();

        var first = await service.PlaceOrderAsync(Authed("user-1"), Command(token, SuccessCard));
        Assert.Equal(CheckoutOutcome.Success, first.Outcome);

        // Simulate a duplicate submission that still sees a populated cart (e.g. concurrent double click).
        var cart = await context.Carts.SingleAsync(c => c.UserId == "user-1");
        context.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 2 });
        await context.SaveChangesAsync();

        var second = await service.PlaceOrderAsync(Authed("user-1"), Command(token, SuccessCard));

        Assert.Equal(CheckoutOutcome.AlreadyPlaced, second.Outcome);
        Assert.Equal(first.OrderNumber, second.OrderNumber);
        Assert.Equal(1, await context.Orders.CountAsync());
        // Stock reduced exactly once.
        Assert.Equal(3, await context.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
    }

    [Fact]
    public async Task Confirmation_is_owner_scoped()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await SeedUserWithCartAsync(context, "owner", stock: 5, normalPrice: 25m, discountPrice: null, cartQty: 1);
        await SeedUserAsync(context, "intruder");
        var service = BuildService(context);
        var placed = await service.PlaceOrderAsync(Authed("owner"), Command(Guid.NewGuid(), SuccessCard));

        Assert.NotNull(await service.GetConfirmationAsync(Authed("owner"), placed.OrderNumber!));
        Assert.Null(await service.GetConfirmationAsync(Authed("intruder"), placed.OrderNumber!));
        Assert.Null(await service.GetConfirmationAsync(Authed("owner"), "ORD-does-not-exist"));
    }

    [Fact]
    public async Task Persisted_order_and_payment_contain_no_full_card_number()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await SeedUserWithCartAsync(context, "user-1", stock: 5, normalPrice: 25m, discountPrice: null, cartQty: 1);
        var service = BuildService(context);

        await service.PlaceOrderAsync(Authed("user-1"), Command(Guid.NewGuid(), SuccessCard));

        var payments = await context.PaymentRecords.AsNoTracking().ToListAsync();
        var orders = await context.Orders.AsNoTracking().Include(o => o.Items).ToListAsync();
        var attempts = await context.CheckoutAttempts.AsNoTracking().ToListAsync();
        var dump = string.Join("|",
            payments.SelectMany(p => new[] { p.ProviderReference, p.ResultCode, p.ResultMessage, p.CardBrand, p.CardLast4 })
                .Concat(orders.SelectMany(o => new[] { o.OrderNumber, o.InvoiceNumber }.Concat(o.Items.Select(i => i.ProductName))))
                .Concat(attempts.Select(a => a.FailureCode))
                .Where(value => value is not null)!);

        Assert.DoesNotContain("4242424242424242", dump);
    }

    [Fact]
    public async Task Concurrent_last_unit_checkout_never_oversells()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await using var database = await SqliteFileTestDatabase.CreateAsync();
            int productId;
            await using (var seed = database.CreateContext())
            {
                var product = await SeedUserWithCartAsync(seed, "user-a", stock: 1, normalPrice: 25m, discountPrice: null, cartQty: 1);
                productId = product.Id;
                await SeedUserWithExistingProductAsync(seed, "user-b", productId, cartQty: 1);
            }

            await using var contextA = database.CreateContext();
            await using var contextB = database.CreateContext();

            var resultA = BuildService(contextA).PlaceOrderAsync(Authed("user-a"), Command(Guid.NewGuid(), SuccessCard));
            var resultB = BuildService(contextB).PlaceOrderAsync(Authed("user-b"), Command(Guid.NewGuid(), SuccessCard));
            var results = await Task.WhenAll(resultA, resultB);

            await using var verify = database.CreateContext();
            Assert.Equal(1, results.Count(r => r.Outcome == CheckoutOutcome.Success));
            Assert.Equal(1, await verify.Orders.CountAsync());
            Assert.Equal(0, await verify.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
        }
    }

    [Fact]
    public async Task Concurrent_multiline_conflict_rolls_back_every_decrement()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await using var database = await SqliteFileTestDatabase.CreateAsync();
            int p1;
            int p2;
            await using (var seed = database.CreateContext())
            {
                // user-a buys plenty of P1 and the single unit of P2; user-b competes for that unit of P2.
                var product1 = await SeedUserWithCartAsync(seed, "user-a", stock: 10, normalPrice: 15m, discountPrice: null, cartQty: 1);
                var product2 = await SeedSecondProductAsync(seed, stock: 1, normalPrice: 30m);
                p1 = product1.Id;
                p2 = product2.Id;
                await AddCartItemAsync(seed, "user-a", p2, cartQty: 1);
                await SeedUserWithExistingProductAsync(seed, "user-b", p2, cartQty: 1);
            }

            await using var contextA = database.CreateContext();
            await using var contextB = database.CreateContext();
            var results = await Task.WhenAll(
                BuildService(contextA).PlaceOrderAsync(Authed("user-a"), Command(Guid.NewGuid(), SuccessCard)),
                BuildService(contextB).PlaceOrderAsync(Authed("user-b"), Command(Guid.NewGuid(), SuccessCard)));

            await using var verify = database.CreateContext();
            var stock1 = await verify.Products.Where(p => p.Id == p1).Select(p => p.StockQuantity).SingleAsync();
            var stock2 = await verify.Products.Where(p => p.Id == p2).Select(p => p.StockQuantity).SingleAsync();
            var orders = await verify.Orders.Include(o => o.Items).ToListAsync();

            Assert.Equal(0, stock2);                       // the single unit is sold exactly once, never oversold
            Assert.Single(orders);                         // only one of the competing checkouts wins
            var aWon = orders[0].Items.Count == 2;
            // If user-a lost the P2 race, its P1 decrement must have been rolled back with the transaction.
            Assert.Equal(aWon ? 9 : 10, stock1);
            Assert.Equal(1, results.Count(r => r.Outcome == CheckoutOutcome.Success));
        }
    }

    private static async Task<Product> SeedSecondProductAsync(ApplicationDbContext db, int stock, decimal normalPrice)
    {
        var category = new Category { Name = $"Cat {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), Slug = Guid.NewGuid().ToString("N") };
        var product = new Product
        {
            Category = category, Name = "Gadget", Slug = Guid.NewGuid().ToString("N"),
            ShortDescription = "Short", FullDescription = "Full", NormalPrice = normalPrice,
            StockQuantity = stock, IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static CheckoutService BuildService(ApplicationDbContext db)
    {
        var clock = new StubClock(Now);
        var calculator = new CartCalculator(Options.Create(new StoreOptions { ShippingFee = 10m }));
        var cartService = new CartService(db, new NoopAnonymousStore(), calculator, clock);
        return new CheckoutService(db, cartService, new FakePaymentService(clock), new OrderNumberGenerator(clock), clock, NullLogger<CheckoutService>.Instance);
    }

    private static ClaimsPrincipal Authed(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth"));

    private static PlaceOrderCommand Command(Guid token, FakePaymentRequest payment) =>
        new(token, "1 Test Street", null, "Testville", "12345", "Testland", "+15551234567", payment);

    private static async Task<Product> SeedUserWithCartAsync(ApplicationDbContext db, string userId, int stock, decimal normalPrice, decimal? discountPrice, int cartQty)
    {
        await SeedUserAsync(db, userId);
        var category = new Category { Name = $"Cat {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), Slug = Guid.NewGuid().ToString("N") };
        var product = new Product
        {
            Category = category, Name = "Widget", Slug = Guid.NewGuid().ToString("N"),
            ShortDescription = "Short", FullDescription = "Full", NormalPrice = normalPrice,
            DiscountPrice = discountPrice, StockQuantity = stock, IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await AddCartItemAsync(db, userId, product.Id, cartQty);
        return product;
    }

    private static async Task SeedUserWithExistingProductAsync(ApplicationDbContext db, string userId, int productId, int cartQty)
    {
        await SeedUserAsync(db, userId);
        await AddCartItemAsync(db, userId, productId, cartQty);
    }

    private static async Task AddCartItemAsync(ApplicationDbContext db, string userId, int productId, int cartQty)
    {
        var cart = await db.Carts.SingleOrDefaultAsync(candidate => candidate.UserId == userId);
        if (cart is null)
        {
            cart = new ShoppingCart { UserId = userId };
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
        }

        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = productId, Quantity = cartQty });
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

    private sealed class NoopAnonymousStore : IAnonymousCartStore
    {
        public IReadOnlyList<AnonymousCartLine> Read() => [];
        public void Write(IEnumerable<AnonymousCartLine> lines) { }
        public void Clear() { }
    }
}
