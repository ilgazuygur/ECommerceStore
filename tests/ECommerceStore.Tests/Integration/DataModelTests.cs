using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Orders;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ECommerceStore.Tests.Integration;

public sealed class DataModelTests
{
    [Fact]
    public async Task Money_round_trips_through_sqlite_integer_columns()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var category = CreateCategory("Audio", "audio");
            context.Add(category);
            context.Add(new Product
            {
                Category = category,
                Name = "Reference Headphones",
                Slug = "reference-headphones",
                ShortDescription = "Reference product",
                FullDescription = "Reference product used by a relational money test.",
                NormalPrice = 129.99m,
                DiscountPrice = 99.95m,
                StockQuantity = 5
            });
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var product = await context.Products.SingleAsync();
            Assert.Equal(129.99m, product.NormalPrice);
            Assert.Equal(99.95m, product.DiscountPrice);
        }

        await using var modelContext = new ApplicationDbContext(database.Options);
        var productType = modelContext.Model.FindEntityType(typeof(Product))!;
        var table = StoreObjectIdentifier.Table("Products", null);
        Assert.Equal("NormalPriceMinor", productType.FindProperty(nameof(Product.NormalPrice))!.GetColumnName(table));
        Assert.Equal(typeof(long), productType.FindProperty(nameof(Product.NormalPrice))!.GetTypeMapping().Converter!.ProviderClrType);
    }

    [Fact]
    public async Task Duplicate_product_slug_is_rejected_by_database()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var category = CreateCategory("Audio", "audio");
        context.Add(category);
        context.Products.AddRange(CreateProduct(category, "first", "duplicate"), CreateProduct(category, "second", "duplicate"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Stale_product_update_raises_concurrency_conflict()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ecommerce-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var category = CreateCategory("Audio", "audio");
                setup.Add(category);
                setup.Add(CreateProduct(category, "Reference", "reference"));
                await setup.SaveChangesAsync();
            }

            await using (var first = new ApplicationDbContext(options))
            await using (var second = new ApplicationDbContext(options))
            {
                var firstProduct = await first.Products.SingleAsync();
                var secondProduct = await second.Products.SingleAsync();
                firstProduct.StockQuantity = 4;
                secondProduct.StockQuantity = 3;
                await first.SaveChangesAsync();

                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Foreign_keys_are_enabled_on_ef_sqlite_connections()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Conditional_stock_claim_never_goes_below_zero()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        int productId;
        await using (var setup = database.CreateContext())
        {
            var category = CreateCategory("Audio", "audio");
            var product = CreateProduct(category, "Only one", "only-one");
            product.StockQuantity = 1;
            setup.Add(product);
            await setup.SaveChangesAsync();
            productId = product.Id;
        }

        await using (var first = database.CreateContext())
        {
            var inventory = new InventoryService(first, new TestClock());
            Assert.True(await inventory.TryDecreaseStockAsync(productId, 1));
        }

        await using (var second = database.CreateContext())
        {
            var inventory = new InventoryService(second, new TestClock());
            Assert.False(await inventory.TryDecreaseStockAsync(productId, 1));
            Assert.Equal(0, await second.Products.Where(product => product.Id == productId).Select(product => product.StockQuantity).SingleAsync());
        }
    }

    [Fact]
    public async Task Rolling_back_multi_line_claim_restores_earlier_stock()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        int availableId;
        int unavailableId;
        await using (var setup = database.CreateContext())
        {
            var category = CreateCategory("Audio", "audio");
            var available = CreateProduct(category, "Available", "available");
            available.StockQuantity = 2;
            var unavailable = CreateProduct(category, "Unavailable", "unavailable");
            unavailable.StockQuantity = 0;
            setup.AddRange(available, unavailable);
            await setup.SaveChangesAsync();
            availableId = available.Id;
            unavailableId = unavailable.Id;
        }

        await using (var context = database.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var inventory = new InventoryService(context, new TestClock());
            Assert.True(await inventory.TryDecreaseStockAsync(availableId, 1));
            Assert.False(await inventory.TryDecreaseStockAsync(unavailableId, 1));
            await transaction.RollbackAsync();
        }

        await using var verification = database.CreateContext();
        Assert.Equal(2, await verification.Products.Where(product => product.Id == availableId).Select(product => product.StockQuantity).SingleAsync());
    }

    private static Category CreateCategory(string name, string slug) => new()
    {
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Slug = slug
    };

    private static Product CreateProduct(Category category, string name, string slug) => new()
    {
        Category = category,
        Name = name,
        Slug = slug,
        ShortDescription = "Short description",
        FullDescription = "Full description",
        NormalPrice = 10m,
        StockQuantity = 5
    };

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
    }
}
