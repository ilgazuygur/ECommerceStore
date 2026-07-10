using System.ComponentModel.DataAnnotations;

namespace ECommerceStore.Web.ViewModels.Catalog;

public enum ProductSort { Newest, PriceLowToHigh, PriceHighToLow, NameAscending, NameDescending }

public sealed class ProductQueryRequest
{
    [StringLength(100)] public string? Keyword { get; set; }
    [StringLength(120)] public string? Category { get; set; }
    [Range(0, 9_999_999_999_999_999.99)] public decimal? MinimumPrice { get; set; }
    [Range(0, 9_999_999_999_999_999.99)] public decimal? MaximumPrice { get; set; }
    public bool InStockOnly { get; set; }
    public ProductSort Sort { get; set; } = ProductSort.Newest;
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
}

public sealed record CategoryOptionViewModel(int Id, string Name, string Slug);

public sealed record ProductCardViewModel(
    int Id, string Name, string Slug, string ShortDescription,
    decimal NormalPrice, decimal? DiscountPrice, int StockQuantity,
    string CategoryName, string CategorySlug, string? ImageUrl,
    bool IsFeatured, DateTime CreatedAtUtc)
{
    public decimal EffectivePrice => DiscountPrice ?? NormalPrice;
    public bool HasDiscount => DiscountPrice.HasValue;
    public bool IsInStock => StockQuantity > 0;
}

public sealed class ProductListViewModel
{
    public required ProductQueryRequest Query { get; init; }
    public required IReadOnlyList<ProductCardViewModel> Products { get; init; }
    public required IReadOnlyList<CategoryOptionViewModel> Categories { get; init; }
    public int TotalCount { get; init; }
    public int PageSize { get; init; }
    public string? ValidationMessage { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPreviousPage => Query.Page > 1;
    public bool HasNextPage => Query.Page < TotalPages;
}

public sealed class ProductDetailsViewModel
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string ShortDescription { get; init; }
    public required string FullDescription { get; init; }
    public decimal NormalPrice { get; init; }
    public decimal? DiscountPrice { get; init; }
    public int StockQuantity { get; init; }
    public required string CategoryName { get; init; }
    public required string CategorySlug { get; init; }
    public string? ImageUrl { get; init; }
    public required IReadOnlyList<ProductCardViewModel> RelatedProducts { get; init; }
    public decimal EffectivePrice => DiscountPrice ?? NormalPrice;
    public bool HasDiscount => DiscountPrice.HasValue;
    public bool IsInStock => StockQuantity > 0;
}

public sealed class HomeViewModel
{
    public required IReadOnlyList<ProductCardViewModel> FeaturedProducts { get; init; }
    public required IReadOnlyList<ProductCardViewModel> LatestProducts { get; init; }
    public required IReadOnlyList<CategoryOptionViewModel> Categories { get; init; }
}
