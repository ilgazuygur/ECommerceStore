using System.Security.Claims;
using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Models.Orders;
using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Images;
using ECommerceStore.Web.Services.Payments;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoreOptions = ECommerceStore.Web.Services.Common.StoreOptions;

namespace ECommerceStore.Tests.Services;

public sealed class AdminServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    private static IOptions<StoreOptions> Store => Options.Create(new StoreOptions { AdminPageSize = 20, LowStockThreshold = 5, ShippingFee = 10m });

    // ---- Products ----

    [Fact]
    public async Task Referenced_product_is_deactivated_not_deleted()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var product = await SeedProductAsync(context, stock: 5);
        await SeedUserAsync(context, "buyer");
        var cart = new ShoppingCart { UserId = "buyer" };
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 1 });
        await context.SaveChangesAsync();

        var result = await new AdminProductService(context, Store, new NoopProductImageService()).DeleteAsync(product.Id);

        Assert.Equal(AdminOutcome.Blocked, result.Outcome);
        Assert.False(await context.Products.Where(p => p.Id == product.Id).Select(p => p.IsActive).SingleAsync());
        Assert.True(await context.Products.AnyAsync(p => p.Id == product.Id)); // still exists
    }

    [Fact]
    public async Task Unreferenced_product_is_deleted()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var product = await SeedProductAsync(context, stock: 5);

        var result = await new AdminProductService(context, Store, new NoopProductImageService()).DeleteAsync(product.Id);

        Assert.True(result.Succeeded);
        Assert.False(await context.Products.AnyAsync(p => p.Id == product.Id));
    }

    [Fact]
    public async Task Stale_product_edit_is_a_concurrency_conflict()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var product = await SeedProductAsync(context, stock: 5);
        var service = new AdminProductService(context, Store, new NoopProductImageService());
        var input = await service.GetForEditAsync(product.Id);

        // Another writer bumps the version first.
        await using (var other = db.CreateContext())
        {
            var tracked = await other.Products.SingleAsync(p => p.Id == product.Id);
            tracked.StockQuantity = 99;
            await other.SaveChangesAsync();
        }

        input!.StockQuantity = 3;
        var result = await service.UpdateAsync(input);
        Assert.Equal(AdminOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task Product_create_validates_business_rules_and_persists_valid_input()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var existing = await SeedProductAsync(context, stock: 5);
        var service = new AdminProductService(context, Store, new NoopProductImageService());
        var valid = ValidProductInput(existing.CategoryId);

        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.NormalPrice = 0m))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.DiscountPrice = -1m))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.DiscountPrice = input.NormalPrice))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.StockQuantity = -1))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.ImageUrl = "https://user:password@example.test/image.png"))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.ImageUrl = "https://example.test/" + new string('a', 2048)))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.ImageUrl = "http://example.test/image.png"))).Outcome);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(WithChanges(valid, input => input.ImageUrl = "ftp://example.test/image.png"))).Outcome);

        var result = await service.CreateAsync(valid);
        Assert.True(result.Succeeded);
        Assert.True(await context.Products.AnyAsync(product => product.Name == valid.Name && product.CategoryId == existing.CategoryId));
    }

    [Fact]
    public async Task Product_edit_does_not_change_historical_order_snapshots()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await PlaceOrderAsync(context, "snapshot-buyer");
        var product = await context.Products.SingleAsync();
        var before = await context.OrderItems.AsNoTracking().SingleAsync();
        var service = new AdminProductService(context, Store, new NoopProductImageService());
        var input = (await service.GetForEditAsync(product.Id))!;
        input.Name = "Renamed Widget";
        input.NormalPrice = 99m;

        Assert.True((await service.UpdateAsync(input)).Succeeded);
        var after = await context.OrderItems.AsNoTracking().SingleAsync();
        Assert.Equal(before.ProductName, after.ProductName);
        Assert.Equal(before.ListUnitPrice, after.ListUnitPrice);
        Assert.Equal(before.PaidUnitPrice, after.PaidUnitPrice);
        Assert.Equal(before.LineTotal, after.LineTotal);
    }

    [Fact]
    public async Task Product_upload_persists_managed_reference_and_remove_cleans_old_file()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var existing = await SeedProductAsync(context, stock: 5);
        var images = new TrackingProductImageService("uploads/products/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
        var service = new AdminProductService(context, Store, images);
        var input = ValidProductInput(existing.CategoryId);
        input.ImageUrl = null;
        input.ImageUpload = DummyUpload();

        Assert.True((await service.CreateAsync(input)).Succeeded);
        var created = await context.Products.SingleAsync(product => product.Name == input.Name);
        Assert.Equal(ProductImageKind.Local, created.ImageKind);
        Assert.Equal(images.SavedPath, created.ImageLocation);

        var edit = (await service.GetForEditAsync(created.Id))!;
        edit.RemoveImage = true;
        Assert.True((await service.UpdateAsync(edit)).Succeeded);
        var updated = await context.Products.AsNoTracking().SingleAsync(product => product.Id == created.Id);
        Assert.Equal(ProductImageKind.None, updated.ImageKind);
        Assert.Null(updated.ImageLocation);
        Assert.Contains(images.SavedPath, images.DeletedPaths);
    }

    [Fact]
    public async Task Stale_product_upload_cleans_new_file_and_preserves_durable_product()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var product = await SeedProductAsync(context, stock: 5);
        var images = new TrackingProductImageService("uploads/products/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");
        var service = new AdminProductService(context, Store, images);
        var edit = (await service.GetForEditAsync(product.Id))!;
        edit.ImageUpload = DummyUpload();

        await using (var other = db.CreateContext())
        {
            var durable = await other.Products.SingleAsync(candidate => candidate.Id == product.Id);
            durable.StockQuantity = 99;
            await other.SaveChangesAsync();
        }

        var result = await service.UpdateAsync(edit);
        Assert.Equal(AdminOutcome.Conflict, result.Outcome);
        Assert.Contains(images.SavedPath, images.DeletedPaths);
        await using var verification = db.CreateContext();
        var persisted = await verification.Products.AsNoTracking().SingleAsync(candidate => candidate.Id == product.Id);
        Assert.Equal(ProductImageKind.None, persisted.ImageKind);
        Assert.Null(persisted.ImageLocation);
    }

    // ---- Categories ----

    [Fact]
    public async Task Duplicate_category_name_is_rejected()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var service = new AdminCategoryService(context, Store);
        Assert.True((await service.CreateAsync(new AdminCategoryInput { Name = "Gadgets" })).Succeeded);

        var duplicate = await service.CreateAsync(new AdminCategoryInput { Name = "gadgets" });
        Assert.Equal(AdminOutcome.Conflict, duplicate.Outcome);
    }

    [Fact]
    public async Task Category_with_products_is_deactivated_not_deleted()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var product = await SeedProductAsync(context, stock: 5);

        var result = await new AdminCategoryService(context, Store).DeleteAsync(product.CategoryId);

        Assert.Equal(AdminOutcome.Blocked, result.Outcome);
        Assert.False(await context.Categories.Where(c => c.Id == product.CategoryId).Select(c => c.IsActive).SingleAsync());
    }

    [Fact]
    public async Task Category_can_be_created_edited_and_safely_deleted_when_unused()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var service = new AdminCategoryService(context, Store);
        Assert.Equal(AdminOutcome.Invalid, (await service.CreateAsync(new AdminCategoryInput { Name = "   " })).Outcome);
        Assert.True((await service.CreateAsync(new AdminCategoryInput { Name = "Desk Gear" })).Succeeded);

        var category = await context.Categories.SingleAsync(candidate => candidate.NormalizedName == "DESK GEAR");
        var edit = (await service.GetForEditAsync(category.Id))!;
        edit.Name = "Office Gear";
        Assert.True((await service.UpdateAsync(edit)).Succeeded);
        Assert.Equal("office-gear", await context.Categories.Where(candidate => candidate.Id == category.Id).Select(candidate => candidate.Slug).SingleAsync());

        Assert.True((await service.DeleteAsync(category.Id)).Succeeded);
        Assert.False(await context.Categories.AnyAsync(candidate => candidate.Id == category.Id));
    }

    // ---- Customers ----

    [Fact]
    public async Task Admin_cannot_disable_self_or_last_administrator()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await SeedRolesAsync(context);
        await SeedUserAsync(context, "admin-1", RoleNames.Administrator);
        await SeedUserAsync(context, "customer-1", RoleNames.Customer);
        var service = new AdminCustomerService(context, Store);

        Assert.Equal(AdminOutcome.Blocked, (await service.SetEnabledAsync("admin-1", false, "admin-1")).Outcome); // self
        Assert.Equal(AdminOutcome.Blocked, (await service.SetEnabledAsync("admin-1", false, "someone-else")).Outcome); // last admin
        Assert.True((await service.SetEnabledAsync("customer-1", false, "admin-1")).Succeeded); // customer ok
        Assert.False(await context.Users.Where(u => u.Id == "customer-1").Select(u => u.IsEnabled).SingleAsync());
    }

    [Fact]
    public async Task Second_administrator_allows_disabling_the_first()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await SeedRolesAsync(context);
        await SeedUserAsync(context, "admin-1", RoleNames.Administrator);
        await SeedUserAsync(context, "admin-2", RoleNames.Administrator);

        var result = await new AdminCustomerService(context, Store).SetEnabledAsync("admin-1", false, "admin-2");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Customer_disable_and_reenable_rotate_security_stamp_and_safe_projection_omits_identity_internals()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await SeedRolesAsync(context);
        await SeedUserAsync(context, "customer-safe", RoleNames.Customer);
        var service = new AdminCustomerService(context, Store);
        var before = await context.Users.Where(user => user.Id == "customer-safe").Select(user => user.SecurityStamp).SingleAsync();

        Assert.True((await service.SetEnabledAsync("customer-safe", false, "admin")).Succeeded);
        var disabledStamp = await context.Users.Where(user => user.Id == "customer-safe").Select(user => user.SecurityStamp).SingleAsync();
        Assert.NotEqual(before, disabledStamp);
        Assert.False((await service.GetDetailsAsync("customer-safe"))!.IsEnabled);

        Assert.True((await service.SetEnabledAsync("customer-safe", true, "admin")).Succeeded);
        var enabledStamp = await context.Users.Where(user => user.Id == "customer-safe").Select(user => user.SecurityStamp).SingleAsync();
        Assert.NotEqual(disabledStamp, enabledStamp);

        var exposedNames = typeof(AdminCustomerDetailsViewModel).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(exposedNames, name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(exposedNames, name => name.Contains("Stamp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(exposedNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customer_search_returns_only_matching_safe_profiles()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await SeedUserAsync(context, "alice");
        await SeedUserAsync(context, "bob");

        var result = await new AdminCustomerService(context, Store).GetListAsync(new AdminCustomerQuery { Search = "alice@" });
        var customer = Assert.Single(result.Customers);
        Assert.Equal("alice", customer.Id);
    }

    // ---- Orders & dashboard ----

    [Fact]
    public async Task Order_status_update_follows_policy_and_preserves_snapshots()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var placed = await PlaceOrderAsync(context, "buyer");
        var order = await context.Orders.AsNoTracking().SingleAsync();
        var service = new AdminOrderService(context, Store);

        // Paid -> Delivered is not allowed.
        var invalid = await service.UpdateStatusAsync(new AdminOrderStatusInput { OrderId = order.Id, NewStatus = "Delivered", Version = order.Version });
        Assert.Equal(AdminOutcome.Invalid, invalid.Outcome);

        // Paid -> Processing is allowed and leaves financial snapshots untouched.
        var valid = await service.UpdateStatusAsync(new AdminOrderStatusInput { OrderId = order.Id, NewStatus = "Processing", Version = order.Version });
        Assert.True(valid.Succeeded);
        var updated = await context.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatus.Processing, updated.OrderStatus);
        Assert.Equal(PaymentStatus.Succeeded, updated.PaymentStatus);
        Assert.Equal(order.GrandTotal, updated.GrandTotal);
        Assert.Equal(placed, updated.OrderNumber);
    }

    [Fact]
    public async Task Dashboard_revenue_counts_only_successful_payments()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await SeedRolesAsync(context);
        await PlaceOrderAsync(context, "buyer"); // paid order, grand total 35 (1 x 25 + 10 shipping)

        var dashboard = await new AdminDashboardService(context, Store).GetAsync();
        Assert.Equal(1, dashboard.TotalOrders);
        Assert.Equal(35m, dashboard.Revenue);
        Assert.Equal(0, dashboard.PendingOrders); // Paid is distinct from Pending.
        Assert.Equal(5, dashboard.LowStockThreshold);
    }

    [Fact]
    public async Task Dashboard_low_stock_excludes_zero_stock_inactive_and_above_threshold_products()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var low = await SeedProductAsync(context, stock: 3);
        var zero = await SeedProductAsync(context, stock: 0);
        var high = await SeedProductAsync(context, stock: 6);
        var inactive = await SeedProductAsync(context, stock: 2);
        inactive.IsActive = false;
        await context.SaveChangesAsync();

        var dashboard = await new AdminDashboardService(context, Store).GetAsync();
        var ids = dashboard.LowStockProducts.Select(product => product.Id).ToArray();
        Assert.Contains(low.Id, ids);
        Assert.DoesNotContain(zero.Id, ids);
        Assert.DoesNotContain(high.Id, ids);
        Assert.DoesNotContain(inactive.Id, ids);
    }

    [Fact]
    public async Task Order_list_searches_number_and_email_and_filters_status()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        var orderNumber = await PlaceOrderAsync(context, "search-buyer");
        var service = new AdminOrderService(context, Store);

        Assert.Single((await service.GetListAsync(new AdminOrderQuery { Search = orderNumber[4..10] })).Orders);
        Assert.Single((await service.GetListAsync(new AdminOrderQuery { Search = "search-buyer@", Status = "Paid" })).Orders);
        Assert.Empty((await service.GetListAsync(new AdminOrderQuery { Status = "Delivered" })).Orders);
    }

    [Fact]
    public async Task Stale_order_status_update_is_a_concurrency_conflict()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await PlaceOrderAsync(context, "stale-order-buyer");
        var order = await context.Orders.AsNoTracking().SingleAsync();
        await using (var other = db.CreateContext())
        {
            var tracked = await other.Orders.SingleAsync(candidate => candidate.Id == order.Id);
            tracked.OrderStatus = OrderStatus.Processing;
            await other.SaveChangesAsync();
        }

        var result = await new AdminOrderService(context, Store).UpdateStatusAsync(new AdminOrderStatusInput
        {
            OrderId = order.Id,
            NewStatus = "Cancelled",
            Version = order.Version
        });
        Assert.Equal(AdminOutcome.Conflict, result.Outcome);
    }

    // ---- Seed helpers ----

    private static async Task<string> PlaceOrderAsync(ApplicationDbContext context, string userId)
    {
        await SeedUserAsync(context, userId);
        var product = await SeedProductAsync(context, stock: 5);
        var cart = new ShoppingCart { UserId = userId };
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 1 });
        await context.SaveChangesAsync();

        var clock = new StubClock(Now);
        var checkout = new CheckoutService(context,
            new CartService(context, new NoopStore(), new CartCalculator(Store), clock),
            new FakePaymentService(clock), new OrderNumberGenerator(clock), clock, NullLogger<CheckoutService>.Instance);
        var result = await checkout.PlaceOrderAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth")),
            new PlaceOrderCommand(Guid.NewGuid(), "1 St", null, "City", "12345", "Country", "+15551234567",
                new FakePaymentRequest("4242424242424242", "Test User", 12, 30, "123")));
        return result.OrderNumber!;
    }

    private static async Task<Product> SeedProductAsync(ApplicationDbContext db, int stock)
    {
        var category = new Category { Name = $"Cat {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), Slug = Guid.NewGuid().ToString("N") };
        var product = new Product
        {
            Category = category, Name = "Widget", Slug = Guid.NewGuid().ToString("N"),
            ShortDescription = "Short", FullDescription = "Full", NormalPrice = 25m, StockQuantity = stock, IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static AdminProductInput ValidProductInput(int categoryId) => new()
    {
        Name = $"Product {Guid.NewGuid():N}",
        CategoryId = categoryId,
        ShortDescription = "A valid short description.",
        FullDescription = "A complete valid product description.",
        NormalPrice = 20m,
        DiscountPrice = 15m,
        StockQuantity = 4,
        IsActive = true,
        ImageUrl = "https://example.test/product.png"
    };

    private static IFormFile DummyUpload()
    {
        var bytes = new byte[] { 1, 2, 3 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "ImageUpload", "photo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    private static AdminProductInput WithChanges(AdminProductInput source, Action<AdminProductInput> change)
    {
        var copy = new AdminProductInput
        {
            Name = source.Name,
            CategoryId = source.CategoryId,
            ShortDescription = source.ShortDescription,
            FullDescription = source.FullDescription,
            NormalPrice = source.NormalPrice,
            DiscountPrice = source.DiscountPrice,
            StockQuantity = source.StockQuantity,
            IsFeatured = source.IsFeatured,
            IsActive = source.IsActive,
            ImageUrl = source.ImageUrl
        };
        change(copy);
        return copy;
    }

    private static async Task SeedRolesAsync(ApplicationDbContext db)
    {
        foreach (var role in new[] { RoleNames.Customer, RoleNames.Administrator })
        {
            if (!await db.Roles.AnyAsync(r => r.Name == role))
            {
                db.Roles.Add(new IdentityRole(role) { Id = role, NormalizedName = role.ToUpperInvariant() });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(ApplicationDbContext db, string userId, string? role = null)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = userId, UserName = $"{userId}@example.test", NormalizedUserName = $"{userId}@EXAMPLE.TEST".ToUpperInvariant(),
                Email = $"{userId}@example.test", NormalizedEmail = $"{userId}@EXAMPLE.TEST".ToUpperInvariant(),
                FirstName = "Test", LastName = "User", IsEnabled = true
            });
            await db.SaveChangesAsync();
        }

        if (role is not null)
        {
            await SeedRolesAsync(db);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role });
            await db.SaveChangesAsync();
        }
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

    private sealed class NoopProductImageService : IProductImageService
    {
        public Task<ProductImageSaveResult> ValidateAndSaveAsync(IFormFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductImageSaveResult.Invalid("Uploads are not configured for this test."));

        public Task<bool> TryDeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class TrackingProductImageService(string savedPath) : IProductImageService
    {
        public string SavedPath { get; } = savedPath;
        public List<string> DeletedPaths { get; } = [];

        public Task<ProductImageSaveResult> ValidateAndSaveAsync(IFormFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductImageSaveResult.Saved(SavedPath));

        public Task<bool> TryDeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            DeletedPaths.Add(relativePath);
            return Task.FromResult(true);
        }
    }
}
