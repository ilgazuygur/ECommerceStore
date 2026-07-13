using System.Text.Json;
using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Assistant;
using ECommerceStore.Web.Services.Catalog;
using ECommerceStore.Web.Services.Common;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Assistant;

public sealed class ProductAssistantToolTests
{
    [Fact]
    public async Task Tools_search_live_public_catalogue_and_escape_wildcards()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var category = new Category { Name = "Electronics", NormalizedName = "ELECTRONICS", Slug = "electronics" };
        db.AddRange(category,
            Product(category, "100% Charger", "charger", 29m, 3),
            Product(category, "Hidden Charger", "hidden", 10m, 2, false),
            Product(category, "Sold Out", "sold-out", 19m, 0),
            Product(category, "Unsafe Image", "unsafe-image", 31m, 1, imageLocation: "http://unsafe.test/image.png"));
        await db.SaveChangesAsync();
        var service = Create(db);

        var wildcard = await service.ExecuteAsync("SearchProducts", Json("""{"keyword":"%","limit":8}"""));
        var inStock = await service.ExecuteAsync("GetInStockProducts", Json("""{"keyword":"charger","limit":8}"""));
        var range = await service.ExecuteAsync("GetProductsWithinPriceRange", Json("""{"minimumPrice":20,"maximumPrice":30,"limit":8}"""));
        var combined = await service.ExecuteAsync("SearchProducts", Json("""{"category":"electronics","maximumPrice":30,"inStockOnly":true,"limit":8}"""));

        Assert.Single(wildcard.Products);
        Assert.Equal("100% Charger", wildcard.Products[0].Name);
        Assert.Single(inStock.Products);
        Assert.Single(range.Products);
        Assert.Single(combined.Products);
        Assert.Equal("100% Charger", combined.Products[0].Name);
        var unsafeImage = Assert.Single((await service.ExecuteAsync("SearchProducts", Json("""{"keyword":"Unsafe Image"}"""))).Products);
        Assert.Null(unsafeImage.ImageUrl);
        Assert.All(wildcard.Products.Concat(inStock.Products).Concat(range.Products), product => Assert.NotEqual("Hidden Charger", product.Name));
    }

    [Fact]
    public async Task Alternative_tool_returns_only_live_in_stock_same_category_candidates()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var electronics = new Category { Name = "Electronics", NormalizedName = "ELECTRONICS", Slug = "electronics" };
        var fitness = new Category { Name = "Fitness", NormalizedName = "FITNESS", Slug = "fitness" };
        var target = Product(electronics, "Target", "target", 100m, 5);
        var cheaperProduct = Product(electronics, "Cheaper", "cheaper", 60m, 3);
        var pricierProduct = Product(electronics, "Pricier", "pricier", 120m, 4);
        var soldOut = Product(electronics, "Sold Out", "sold-out-alternative", 50m, 0);
        var otherCategory = Product(fitness, "Other Category", "other-category", 20m, 8);
        db.AddRange(electronics, fitness, target,
            cheaperProduct, pricierProduct, soldOut, otherCategory);
        await db.SaveChangesAsync();
        var service = Create(db);
        var candidateIds = string.Join(",", new[] { cheaperProduct.Id, pricierProduct.Id, soldOut.Id, otherCategory.Id });

        var alternatives = await service.ExecuteAsync("GetProductAlternatives",
            Json($$"""{"productId":{{target.Id}},"candidateProductIds":[{{candidateIds}}],"cheaperOnly":false,"limit":4}"""));
        var cheaper = await service.ExecuteAsync("GetProductAlternatives",
            Json($$"""{"productId":{{target.Id}},"candidateProductIds":[{{candidateIds}}],"cheaperOnly":true,"limit":4}"""));

        Assert.Equal(["Cheaper", "Pricier"], alternatives.Products.Select(product => product.Name));
        Assert.Equal("Cheaper", Assert.Single(cheaper.Products).Name);
    }

    [Theory]
    [InlineData("SearchProducts", "{\"keyword\":\"x\",\"unknown\":true}")]
    [InlineData("GetProductDetails", "{\"productId\":0}")]
    [InlineData("GetProductAlternatives", "{\"productId\":0}")]
    [InlineData("GetProductsByCategory", "{\"category\":\"\"}")]
    [InlineData("GetProductsWithinPriceRange", "{\"minimumPrice\":20,\"maximumPrice\":10}")]
    [InlineData("UnregisteredTool", "{}")]
    public async Task Invalid_tool_arguments_are_rejected(string tool, string arguments)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var service = Create(db);
        await Assert.ThrowsAsync<AssistantToolValidationException>(() => service.ExecuteAsync(tool, Json(arguments)));
    }

    private static ProductAssistantToolService Create(ECommerceStore.Web.Data.ApplicationDbContext db) =>
        new(new ProductQueryService(db, Options.Create(new StoreOptions())));

    private static Product Product(Category category, string name, string slug, decimal price, int stock, bool active = true, string? imageLocation = null) => new()
    {
        Category = category, Name = name, Slug = slug, ShortDescription = name,
        FullDescription = name, NormalPrice = price, StockQuantity = stock, IsActive = active,
        ImageKind = imageLocation is null ? ProductImageKind.None : ProductImageKind.ExternalUrl,
        ImageLocation = imageLocation
    };

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
