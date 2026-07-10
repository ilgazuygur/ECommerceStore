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
        ProductImageKind.Local when !string.IsNullOrWhiteSpace(location) => "/" + location.TrimStart('/'),
        ProductImageKind.ExternalUrl when Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps => uri.AbsoluteUri,
        _ => null
    };
}
