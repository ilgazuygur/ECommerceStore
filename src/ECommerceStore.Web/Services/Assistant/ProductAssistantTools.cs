using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Services.Assistant.Provider;
using ECommerceStore.Web.Services.Catalog;

namespace ECommerceStore.Web.Services.Assistant;

public sealed record AssistantToolExecutionResult(string Json, IReadOnlyList<PublicProductResult> Products);

public sealed class AssistantToolValidationException(string message) : Exception(message);

public interface IProductAssistantToolService
{
    IReadOnlyList<AssistantToolDefinition> Definitions { get; }
    Task<AssistantToolExecutionResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default);
}

public sealed class ProductAssistantToolService(IProductQueryService products) : IProductAssistantToolService
{
    private static readonly JsonSerializerOptions ArgumentOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public IReadOnlyList<AssistantToolDefinition> Definitions { get; } =
    [
        Definition("SearchProducts", "Search active public catalogue products by a short keyword.",
            """{"type":"object","properties":{"keyword":{"type":["string","null"],"maxLength":100},"category":{"type":["string","null"],"maxLength":120},"minimumPrice":{"type":["number","null"],"minimum":0},"maximumPrice":{"type":["number","null"],"minimum":0},"inStockOnly":{"type":"boolean"},"limit":{"type":"integer","minimum":1,"maximum":8}},"additionalProperties":false}"""),
        Definition("GetProductDetails", "Reload one active public product by its database id.",
            """{"type":"object","properties":{"productId":{"type":"integer","minimum":1}},"required":["productId"],"additionalProperties":false}"""),
        Definition("GetProductsByCategory", "List active public products in an active category.",
            """{"type":"object","properties":{"category":{"type":"string","maxLength":120},"limit":{"type":"integer","minimum":1,"maximum":8}},"required":["category"],"additionalProperties":false}"""),
        Definition("GetProductsWithinPriceRange", "Find active public products in a range of current store-currency prices.",
            """{"type":"object","properties":{"minimumPrice":{"type":"number","minimum":0},"maximumPrice":{"type":"number","minimum":0},"keyword":{"type":["string","null"],"maxLength":100},"limit":{"type":"integer","minimum":1,"maximum":8}},"required":["minimumPrice","maximumPrice"],"additionalProperties":false}"""),
        Definition("GetInStockProducts", "Find active public products whose current stock is greater than zero.",
            """{"type":"object","properties":{"keyword":{"type":["string","null"],"maxLength":100},"limit":{"type":"integer","minimum":1,"maximum":8}},"additionalProperties":false}""")
    ];

    public async Task<AssistantToolExecutionResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PublicProductResult> result = name switch
        {
            "SearchProducts" => await SearchAsync(Read<SearchArguments>(arguments), cancellationToken),
            "GetProductDetails" => await DetailsAsync(Read<DetailsArguments>(arguments), cancellationToken),
            "GetProductsByCategory" => await CategoryAsync(Read<CategoryArguments>(arguments), cancellationToken),
            "GetProductsWithinPriceRange" => await PriceAsync(Read<PriceArguments>(arguments), cancellationToken),
            "GetInStockProducts" => await InStockAsync(Read<SearchArguments>(arguments), cancellationToken),
            _ => throw new AssistantToolValidationException("The requested catalogue tool is not registered.")
        };

        var safe = result.Select(product => new
        {
            product.Id,
            product.Name,
            product.EffectivePrice,
            product.StockQuantity,
            product.CategoryName,
            product.ProductUrl
        });
        return new AssistantToolExecutionResult(JsonSerializer.Serialize(safe), result);
    }

    private Task<IReadOnlyList<PublicProductResult>> SearchAsync(SearchArguments input, CancellationToken token)
    {
        ValidateKeyword(input.Keyword);
        ValidateCategory(input.Category);
        ValidatePriceRange(input.MinimumPrice, input.MaximumPrice);
        return products.SearchForAssistantAsync(new AssistantProductQuery(
            Keyword: NullIfWhiteSpace(input.Keyword), Category: NullIfWhiteSpace(input.Category),
            MinimumPrice: input.MinimumPrice, MaximumPrice: input.MaximumPrice,
            InStockOnly: input.InStockOnly, Limit: Limit(input.Limit)), token);
    }

    private async Task<IReadOnlyList<PublicProductResult>> DetailsAsync(DetailsArguments input, CancellationToken token)
    {
        if (input.ProductId <= 0) throw new AssistantToolValidationException("productId must be a positive integer.");
        var result = await products.GetPublicProductsByIdsAsync([input.ProductId], token);
        return result.TryGetValue(input.ProductId, out var product) ? [product] : [];
    }

    private Task<IReadOnlyList<PublicProductResult>> CategoryAsync(CategoryArguments input, CancellationToken token)
    {
        var category = input.Category?.Trim();
        if (string.IsNullOrWhiteSpace(category))
            throw new AssistantToolValidationException("category is required and must not exceed 120 characters.");
        ValidateCategory(category);
        return products.SearchForAssistantAsync(new AssistantProductQuery(Category: category, Limit: Limit(input.Limit)), token);
    }

    private Task<IReadOnlyList<PublicProductResult>> PriceAsync(PriceArguments input, CancellationToken token)
    {
        ValidateKeyword(input.Keyword);
        ValidatePriceRange(input.MinimumPrice, input.MaximumPrice);
        return products.SearchForAssistantAsync(new AssistantProductQuery(
            Keyword: NullIfWhiteSpace(input.Keyword), MinimumPrice: input.MinimumPrice,
            MaximumPrice: input.MaximumPrice, Limit: Limit(input.Limit)), token);
    }

    private Task<IReadOnlyList<PublicProductResult>> InStockAsync(SearchArguments input, CancellationToken token)
    {
        ValidateKeyword(input.Keyword);
        return products.SearchForAssistantAsync(new AssistantProductQuery(
            Keyword: NullIfWhiteSpace(input.Keyword), InStockOnly: true, Limit: Limit(input.Limit)), token);
    }

    private static T Read<T>(JsonElement arguments) where T : class
    {
        try
        {
            return arguments.Deserialize<T>(ArgumentOptions) ?? throw new AssistantToolValidationException("Tool arguments are required.");
        }
        catch (JsonException)
        {
            throw new AssistantToolValidationException("Tool arguments were malformed.");
        }
    }

    private static void ValidateKeyword(string? keyword)
    {
        if (keyword?.Trim().Length > 100) throw new AssistantToolValidationException("keyword must not exceed 100 characters.");
    }

    private static void ValidateCategory(string? category)
    {
        if (category?.Trim().Length > 120) throw new AssistantToolValidationException("category must not exceed 120 characters.");
    }

    private static void ValidatePriceRange(decimal? minimum, decimal? maximum)
    {
        if (minimum is < 0 || maximum is < 0 || minimum > Money.MaxAmount || maximum > Money.MaxAmount ||
            minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new AssistantToolValidationException("The price range is invalid.");
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int Limit(int? value) => Math.Clamp(value ?? 6, 1, 8);

    private static AssistantToolDefinition Definition(string name, string description, string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return new AssistantToolDefinition(name, description, document.RootElement.Clone());
    }

    private sealed class SearchArguments
    {
        public string? Keyword { get; init; }
        public string? Category { get; init; }
        public decimal? MinimumPrice { get; init; }
        public decimal? MaximumPrice { get; init; }
        public bool InStockOnly { get; init; }
        public int? Limit { get; init; }
    }
    private sealed class DetailsArguments { public int ProductId { get; init; } }
    private sealed class CategoryArguments { public string? Category { get; init; } public int? Limit { get; init; } }
    private sealed class PriceArguments
    {
        public decimal MinimumPrice { get; init; }
        public decimal MaximumPrice { get; init; }
        public string? Keyword { get; init; }
        public int? Limit { get; init; }
    }
}
