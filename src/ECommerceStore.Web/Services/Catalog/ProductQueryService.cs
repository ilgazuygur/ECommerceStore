using System.Linq.Expressions;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Catalog;

public interface IProductQueryService
{
    Task<HomeViewModel> GetHomeAsync(CancellationToken cancellationToken = default);
    Task<ProductListViewModel> SearchAsync(ProductQueryRequest request, CancellationToken cancellationToken = default);
    Task<ProductDetailsViewModel?> GetDetailsAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicProductResult>> SearchForAssistantAsync(AssistantProductQuery request, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, PublicProductResult>> GetPublicProductsByIdsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default);
}

public sealed record AssistantProductQuery(
    string? Keyword = null,
    string? Category = null,
    decimal? MinimumPrice = null,
    decimal? MaximumPrice = null,
    bool InStockOnly = false,
    IReadOnlyCollection<int>? ProductIds = null,
    int Limit = 6);

public sealed record PublicProductResult(
    int Id,
    string Name,
    string Slug,
    string ShortDescription,
    decimal NormalPrice,
    decimal? DiscountPrice,
    int StockQuantity,
    string CategoryName,
    string CategorySlug,
    string? ImageUrl)
{
    public decimal EffectivePrice => DiscountPrice ?? NormalPrice;
    public bool IsInStock => StockQuantity > 0;
    public string ProductUrl => $"/products/{Slug}";
}

public sealed class ProductQueryService(ApplicationDbContext db, IOptions<StoreOptions> options) : IProductQueryService
{
    private readonly StoreOptions _store = options.Value;

    public async Task<HomeViewModel> GetHomeAsync(CancellationToken cancellationToken = default)
    {
        var publicProducts = PublicProducts();
        return new HomeViewModel
        {
            FeaturedProducts = await publicProducts.Where(product => product.IsFeatured)
                .OrderByDescending(product => product.CreatedAtUtc).ThenByDescending(product => product.Id)
                .Take(8).Select(CardProjection).ToListAsync(cancellationToken),
            LatestProducts = await publicProducts
                .OrderByDescending(product => product.CreatedAtUtc).ThenByDescending(product => product.Id)
                .Take(8).Select(CardProjection).ToListAsync(cancellationToken),
            Categories = await GetCategoriesAsync(cancellationToken)
        };
    }

    public async Task<ProductListViewModel> SearchAsync(ProductQueryRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request, out var message);
        IQueryable<Product> query = PublicProducts();

        if (!string.IsNullOrWhiteSpace(normalized.Category))
            query = query.Where(product => product.Category.Slug == normalized.Category);

        if (!string.IsNullOrWhiteSpace(normalized.Keyword))
        {
            var pattern = $"%{EscapeLike(normalized.Keyword)}%";
            query = query.Where(product =>
                EF.Functions.Like(product.Name, pattern, "\\") ||
                EF.Functions.Like(product.ShortDescription, pattern, "\\") ||
                EF.Functions.Like(product.FullDescription, pattern, "\\"));
        }

        if (normalized.MinimumPrice.HasValue)
            query = query.Where(product => (product.DiscountPrice ?? product.NormalPrice) >= normalized.MinimumPrice.Value);
        if (normalized.MaximumPrice.HasValue)
            query = query.Where(product => (product.DiscountPrice ?? product.NormalPrice) <= normalized.MaximumPrice.Value);
        if (normalized.InStockOnly)
            query = query.Where(product => product.StockQuantity > 0);

        query = normalized.Sort switch
        {
            ProductSort.PriceLowToHigh => query.OrderBy(product => product.DiscountPrice ?? product.NormalPrice).ThenBy(product => product.Id),
            ProductSort.PriceHighToLow => query.OrderByDescending(product => product.DiscountPrice ?? product.NormalPrice).ThenBy(product => product.Id),
            ProductSort.NameAscending => query.OrderBy(product => product.Name).ThenBy(product => product.Id),
            ProductSort.NameDescending => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
            _ => query.OrderByDescending(product => product.CreatedAtUtc).ThenByDescending(product => product.Id)
        };

        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)_store.CatalogPageSize));
        normalized.Page = Math.Min(normalized.Page, totalPages);

        return new ProductListViewModel
        {
            Query = normalized,
            Products = await query.Skip((normalized.Page - 1) * _store.CatalogPageSize)
                .Take(_store.CatalogPageSize).Select(CardProjection).ToListAsync(cancellationToken),
            Categories = await GetCategoriesAsync(cancellationToken),
            TotalCount = count,
            PageSize = _store.CatalogPageSize,
            ValidationMessage = message
        };
    }

    public async Task<ProductDetailsViewModel?> GetDetailsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedSlug.Length is 0 or > 220) return null;

        var product = await PublicProducts().Where(item => item.Slug == normalizedSlug)
            .Select(item => new
            {
                item.Id, item.Name, item.Slug, item.ShortDescription, item.FullDescription,
                item.NormalPrice, item.DiscountPrice, item.StockQuantity,
                CategoryName = item.Category.Name, CategorySlug = item.Category.Slug,
                item.ImageKind, item.ImageLocation, item.CategoryId
            }).SingleOrDefaultAsync(cancellationToken);
        if (product is null) return null;

        var related = await PublicProducts()
            .Where(item => item.CategoryId == product.CategoryId && item.Id != product.Id)
            .OrderByDescending(item => item.IsFeatured).ThenByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Take(4).Select(CardProjection).ToListAsync(cancellationToken);

        return new ProductDetailsViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            ShortDescription = product.ShortDescription,
            FullDescription = product.FullDescription,
            NormalPrice = product.NormalPrice,
            DiscountPrice = product.DiscountPrice,
            StockQuantity = product.StockQuantity,
            CategoryName = product.CategoryName,
            CategorySlug = product.CategorySlug,
            ImageUrl = ResolveImageUrl(product.ImageKind, product.ImageLocation),
            RelatedProducts = related
        };
    }

    public async Task<IReadOnlyList<PublicProductResult>> SearchForAssistantAsync(
        AssistantProductQuery request,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(request.Limit, 1, 8);
        var keyword = request.Keyword?.Trim();
        var category = request.Category?.Trim().ToLowerInvariant();
        if (keyword?.Length > 100 || category?.Length > 120)
            return [];

        IQueryable<Product> query = PublicProducts();
        if (request.ProductIds is { Count: > 0 })
        {
            var ids = request.ProductIds.Where(id => id > 0).Distinct().Take(20).ToArray();
            query = query.Where(product => ids.Contains(product.Id));
        }
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(product => product.Category.Slug == category || product.Category.Name.ToLower() == category);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{EscapeLike(keyword)}%";
            query = query.Where(product =>
                EF.Functions.Like(product.Name, pattern, "\\") ||
                EF.Functions.Like(product.ShortDescription, pattern, "\\") ||
                EF.Functions.Like(product.FullDescription, pattern, "\\"));
        }
        if (request.MinimumPrice.HasValue)
            query = query.Where(product => (product.DiscountPrice ?? product.NormalPrice) >= request.MinimumPrice.Value);
        if (request.MaximumPrice.HasValue)
            query = query.Where(product => (product.DiscountPrice ?? product.NormalPrice) <= request.MaximumPrice.Value);
        if (request.InStockOnly)
            query = query.Where(product => product.StockQuantity > 0);

        var rows = await query.OrderBy(product => product.DiscountPrice ?? product.NormalPrice).ThenBy(product => product.Id)
            .Take(limit)
            .Select(product => new
            {
                product.Id, product.Name, product.Slug, product.ShortDescription,
                product.NormalPrice, product.DiscountPrice, product.StockQuantity,
                CategoryName = product.Category.Name, CategorySlug = product.Category.Slug,
                product.ImageKind, product.ImageLocation
            })
            .ToListAsync(cancellationToken);
        return rows.Select(product => new PublicProductResult(
            product.Id, product.Name, product.Slug, product.ShortDescription,
            product.NormalPrice, product.DiscountPrice, product.StockQuantity,
            product.CategoryName, product.CategorySlug, ResolveImageUrl(product.ImageKind, product.ImageLocation))).ToList();
    }

    public async Task<IReadOnlyDictionary<int, PublicProductResult>> GetPublicProductsByIdsAsync(
        IEnumerable<int> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = productIds.Where(id => id > 0).Distinct().Take(100).ToArray();
        if (ids.Length == 0) return new Dictionary<int, PublicProductResult>();
        var rows = await PublicProducts().Where(product => ids.Contains(product.Id))
            .Select(product => new
            {
                product.Id, product.Name, product.Slug, product.ShortDescription,
                product.NormalPrice, product.DiscountPrice, product.StockQuantity,
                CategoryName = product.Category.Name, CategorySlug = product.Category.Slug,
                product.ImageKind, product.ImageLocation
            })
            .ToListAsync(cancellationToken);
        var products = rows.Select(product => new PublicProductResult(
            product.Id, product.Name, product.Slug, product.ShortDescription,
            product.NormalPrice, product.DiscountPrice, product.StockQuantity,
            product.CategoryName, product.CategorySlug, ResolveImageUrl(product.ImageKind, product.ImageLocation))).ToList();
        return products.ToDictionary(product => product.Id);
    }

    private IQueryable<Product> PublicProducts() => db.Products.AsNoTracking()
        .Where(product => product.IsActive && product.Category.IsActive);

    private async Task<IReadOnlyList<CategoryOptionViewModel>> GetCategoriesAsync(CancellationToken token) =>
        await db.Categories.AsNoTracking().Where(category => category.IsActive)
            .OrderBy(category => category.Name).ThenBy(category => category.Id)
            .Select(category => new CategoryOptionViewModel(category.Id, category.Name, category.Slug)).ToListAsync(token);

    private static readonly Expression<Func<Product, ProductCardViewModel>> CardProjection = product =>
        new ProductCardViewModel(product.Id, product.Name, product.Slug, product.ShortDescription,
            product.NormalPrice, product.DiscountPrice, product.StockQuantity,
            product.Category.Name, product.Category.Slug,
            product.ImageKind == ProductImageKind.Local ? "/" + product.ImageLocation :
            product.ImageKind == ProductImageKind.ExternalUrl ? product.ImageLocation : null,
            product.IsFeatured, product.CreatedAtUtc);

    private static ProductQueryRequest Normalize(ProductQueryRequest request, out string? message)
    {
        var keyword = request.Keyword?.Trim();
        var category = request.Category?.Trim().ToLowerInvariant();
        message = null;
        if (keyword?.Length > 100)
        {
            keyword = keyword[..100];
            message = "The search keyword was shortened to 100 characters.";
        }
        var minimum = request.MinimumPrice is >= 0 and <= Money.MaxAmount ? request.MinimumPrice : null;
        var maximum = request.MaximumPrice is >= 0 and <= Money.MaxAmount ? request.MaximumPrice : null;
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
        {
            maximum = null;
            message = "Minimum price cannot be greater than maximum price.";
        }
        return new ProductQueryRequest
        {
            Keyword = keyword,
            Category = category is { Length: > 0 and <= 120 } ? category : null,
            MinimumPrice = minimum,
            MaximumPrice = maximum,
            InStockOnly = request.InStockOnly,
            Sort = Enum.IsDefined(request.Sort) ? request.Sort : ProductSort.Newest,
            Page = Math.Max(1, request.Page)
        };
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string? ResolveImageUrl(ProductImageKind kind, string? location) => kind switch
    {
        ProductImageKind.Local when IsSafeManagedImagePath(location) => "/" + location!.TrimStart('/'),
        ProductImageKind.ExternalUrl when Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) => uri.AbsoluteUri,
        _ => null
    };

    private static bool IsSafeManagedImagePath(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return false;
        var normalized = location.TrimStart('/');
        return normalized.StartsWith("uploads/products/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("..", StringComparison.Ordinal) && !normalized.Contains('\\');
    }
}
