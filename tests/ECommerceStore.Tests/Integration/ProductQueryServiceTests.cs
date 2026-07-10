using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Catalog;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Catalog;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Integration;

public sealed class ProductQueryServiceTests
{
    [Fact]
    public async Task Search_combines_filters_sort_and_stable_pagination()
    {
        await using var database = await CreateCatalogAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context, pageSize: 2);

        var result = await service.SearchAsync(new ProductQueryRequest
        {
            Category = "electronics",
            Keyword = "wireless",
            MinimumPrice = 20m,
            MaximumPrice = 100m,
            InStockOnly = true,
            Sort = ProductSort.PriceHighToLow,
            Page = 1
        });

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.Products.Count);
        Assert.Equal(new[] { 80m, 60m }, result.Products.Select(product => product.EffectivePrice));
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literal_text()
    {
        await using var database = await CreateCatalogAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var result = await service.SearchAsync(new ProductQueryRequest { Keyword = "%" });

        Assert.Single(result.Products);
        Assert.Equal("100% Wireless Charger", result.Products[0].Name);
    }

    [Fact]
    public async Task Public_queries_hide_inactive_products_and_categories()
    {
        await using var database = await CreateCatalogAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var list = await service.SearchAsync(new ProductQueryRequest());
        var hiddenProduct = await service.GetDetailsAsync("hidden-product");
        var hiddenCategoryProduct = await service.GetDetailsAsync("hidden-category-product");

        Assert.DoesNotContain(list.Products, product => product.Slug.StartsWith("hidden", StringComparison.Ordinal));
        Assert.Null(hiddenProduct);
        Assert.Null(hiddenCategoryProduct);
    }

    [Fact]
    public async Task Details_contains_required_data_and_at_most_four_related_products()
    {
        await using var database = await CreateCatalogAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var details = await service.GetDetailsAsync("wireless-60");

        Assert.NotNull(details);
        Assert.Equal("Electronics", details!.CategoryName);
        Assert.Equal(60m, details.EffectivePrice);
        Assert.True(details.IsInStock);
        Assert.InRange(details.RelatedProducts.Count, 1, 4);
        Assert.DoesNotContain(details.RelatedProducts, product => product.Id == details.Id);
    }

    [Fact]
    public async Task Home_sections_only_use_public_products()
    {
        await using var database = await CreateCatalogAsync();
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var home = await service.GetHomeAsync();

        Assert.NotEmpty(home.FeaturedProducts);
        Assert.All(home.FeaturedProducts, product => Assert.True(product.IsFeatured));
        Assert.DoesNotContain(home.LatestProducts, product => product.Slug.StartsWith("hidden", StringComparison.Ordinal));
        Assert.DoesNotContain(home.Categories, category => category.Slug == "hidden");
    }

    private static ProductQueryService CreateService(ECommerceStore.Web.Data.ApplicationDbContext context, int pageSize = 12) =>
        new(context, Options.Create(new StoreOptions { CatalogPageSize = pageSize }));

    private static async Task<SqliteTestDatabase> CreateCatalogAsync()
    {
        var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var electronics = Category("Electronics", "electronics");
        var home = Category("Home", "home");
        var hiddenCategory = Category("Hidden", "hidden", false);
        context.Categories.AddRange(electronics, home, hiddenCategory);
        context.Products.AddRange(
            Product(electronics, "Wireless 40", "wireless-40", 40m, true, true),
            Product(electronics, "Wireless 60", "wireless-60", 60m, true, true),
            Product(electronics, "Wireless 80", "wireless-80", 80m, true, false),
            Product(electronics, "Wireless 120", "wireless-120", 120m, true, false),
            Product(electronics, "100% Wireless Charger", "literal-percent", 35m, true, false),
            Product(electronics, "Hidden Product", "hidden-product", 30m, false, true),
            Product(home, "Home Product", "home-product", 25m, true, false),
            Product(hiddenCategory, "Hidden Category Product", "hidden-category-product", 25m, true, true));
        await context.SaveChangesAsync();
        return database;
    }

    private static Category Category(string name, string slug, bool active = true) => new()
    {
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Slug = slug,
        IsActive = active
    };

    private static Product Product(Category category, string name, string slug, decimal price, bool active, bool featured) => new()
    {
        Category = category,
        Name = name,
        Slug = slug,
        ShortDescription = $"{name} short description",
        FullDescription = $"{name} full description with wireless details.",
        NormalPrice = price,
        StockQuantity = slug == "wireless-120" ? 0 : 10,
        IsActive = active,
        IsFeatured = featured
    };
}
