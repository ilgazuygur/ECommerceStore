using System.Text;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Images;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Admin;

public interface IAdminProductService
{
    Task<AdminProductListViewModel> GetListAsync(AdminProductQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryOption>> GetCategoryOptionsAsync(CancellationToken cancellationToken = default);
    Task<AdminProductInput?> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<AdminResult> CreateAsync(AdminProductInput input, CancellationToken cancellationToken = default);
    Task<AdminResult> UpdateAsync(AdminProductInput input, CancellationToken cancellationToken = default);
    Task<AdminResult> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);
    Task<AdminResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class AdminProductService(
    ApplicationDbContext db,
    IOptions<StoreOptions> storeOptions,
    IProductImageService images) : IAdminProductService
{
    private readonly int _pageSize = storeOptions.Value.AdminPageSize;

    public async Task<AdminProductListViewModel> GetListAsync(AdminProductQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var products = db.Products.AsNoTracking().Include(product => product.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            products = products.Where(product => EF.Functions.Like(product.Name, $"%{keyword}%"));
        }

        if (query.CategoryId is > 0)
        {
            products = products.Where(product => product.CategoryId == query.CategoryId);
        }

        if (query.ActiveOnly == true)
        {
            products = products.Where(product => product.IsActive);
        }

        var total = await products.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)_pageSize));
        page = Math.Min(page, totalPages);

        var items = await products
            .OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize)
            .Select(product => new AdminProductListItem(
                product.Id, product.Name, product.Category.Name, product.NormalPrice, product.DiscountPrice,
                product.StockQuantity, product.IsActive, product.IsFeatured))
            .ToListAsync(cancellationToken);

        return new AdminProductListViewModel
        {
            Query = query,
            Products = items,
            Categories = await GetCategoryOptionsAsync(cancellationToken),
            Page = page,
            PageSize = _pageSize,
            TotalCount = total
        };
    }

    public async Task<IReadOnlyList<CategoryOption>> GetCategoryOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Categories.AsNoTracking().OrderBy(category => category.Name)
            .Select(category => new CategoryOption(category.Id, category.Name, category.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<AdminProductInput?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        return new AdminProductInput
        {
            Id = product.Id,
            Name = product.Name,
            CategoryId = product.CategoryId,
            ShortDescription = product.ShortDescription,
            FullDescription = product.FullDescription,
            NormalPrice = product.NormalPrice,
            DiscountPrice = product.DiscountPrice,
            StockQuantity = product.StockQuantity,
            IsFeatured = product.IsFeatured,
            IsActive = product.IsActive,
            ImageUrl = product.ImageKind == ProductImageKind.ExternalUrl ? product.ImageLocation : null,
            Version = product.Version
        };
    }

    public async Task<AdminResult> CreateAsync(AdminProductInput input, CancellationToken cancellationToken = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return validation;
        }

        if (!await db.Categories.AnyAsync(category => category.Id == input.CategoryId, cancellationToken))
        {
            return AdminResult.Invalid("Choose an existing category.");
        }

        var product = new Product { Slug = await GenerateSlugAsync(input.Name, null, cancellationToken) };
        var image = await SelectImageAsync(input, ProductImageKind.None, null, isCreate: true, cancellationToken);
        if (image.Error is not null)
        {
            return AdminResult.Invalid(image.Error);
        }

        Apply(product, input);
        product.ImageKind = image.Kind;
        product.ImageLocation = image.Location;
        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AdminResult.Ok("Product created.");
        }
        catch (DbUpdateException)
        {
            await CleanupNewImageAsync(image, cancellationToken);
            return AdminResult.Conflict("The product could not be saved. No uploaded file was retained.");
        }
    }

    public async Task<AdminResult> UpdateAsync(AdminProductInput input, CancellationToken cancellationToken = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return validation;
        }

        var product = await db.Products.SingleOrDefaultAsync(candidate => candidate.Id == input.Id, cancellationToken);
        if (product is null)
        {
            return AdminResult.NotFound();
        }

        if (!await db.Categories.AnyAsync(category => category.Id == input.CategoryId, cancellationToken))
        {
            return AdminResult.Invalid("Choose an existing category.");
        }

        if (product.Name != input.Name)
        {
            product.Slug = await GenerateSlugAsync(input.Name, product.Id, cancellationToken);
        }

        var oldKind = product.ImageKind;
        var oldLocation = product.ImageLocation;
        var image = await SelectImageAsync(input, oldKind, oldLocation, isCreate: false, cancellationToken);
        if (image.Error is not null)
        {
            return AdminResult.Invalid(image.Error);
        }

        Apply(product, input);
        product.ImageKind = image.Kind;
        product.ImageLocation = image.Location;
        db.Entry(product).Property(candidate => candidate.Version).OriginalValue = input.Version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var cleaned = await CleanupOldImageAsync(product.Id, oldKind, oldLocation, image, cancellationToken);
            return AdminResult.Ok(cleaned ? "Product updated." : "Product updated, but the previous managed image could not be cleaned up safely.");
        }
        catch (DbUpdateConcurrencyException)
        {
            await CleanupNewImageAsync(image, cancellationToken);
            return AdminResult.Conflict("This product was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException)
        {
            await CleanupNewImageAsync(image, cancellationToken);
            return AdminResult.Conflict("The product could not be updated. No new uploaded file was retained.");
        }
    }

    public async Task<AdminResult> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (product is null)
        {
            return AdminResult.NotFound();
        }

        product.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return AdminResult.Ok(active ? "Product activated." : "Product deactivated.");
    }

    public async Task<AdminResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (product is null)
        {
            return AdminResult.NotFound();
        }

        // Hard delete is only safe when no historical order references the product; otherwise deactivate so
        // order snapshots (which keep their own name/price copies) and referential integrity are preserved.
        var referenced = await db.OrderItems.AnyAsync(item => item.ProductId == id, cancellationToken)
            || await db.CartItems.AnyAsync(item => item.ProductId == id, cancellationToken);
        if (referenced)
        {
            if (!product.IsActive)
            {
                return AdminResult.Blocked("This product is referenced by existing orders or carts and is already deactivated, so it cannot be deleted.");
            }

            product.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
            return AdminResult.Blocked("This product is referenced by existing orders or carts, so it was deactivated instead of deleted.");
        }

        var oldKind = product.ImageKind;
        var oldLocation = product.ImageLocation;
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        var cleaned = await CleanupOldImageAsync(id, oldKind, oldLocation,
            new ImageSelection(ProductImageKind.None, null, false, null), cancellationToken);
        return AdminResult.Ok(cleaned ? "Product deleted." : "Product deleted, but its managed image could not be cleaned up safely.");
    }

    private static AdminResult? Validate(AdminProductInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.ShortDescription) ||
            string.IsNullOrWhiteSpace(input.FullDescription))
        {
            return AdminResult.Invalid("Name and descriptions are required.");
        }

        if (input.NormalPrice <= 0 || input.NormalPrice > 9_999_999.99m || input.NormalPrice != Money.Round(input.NormalPrice))
        {
            return AdminResult.Invalid("The normal price must be positive and have at most two decimal places.");
        }

        if (input.DiscountPrice is < 0 ||
            input.DiscountPrice is > 9_999_999.99m ||
            input.DiscountPrice is { } preciseDiscount && preciseDiscount != Money.Round(preciseDiscount))
        {
            return AdminResult.Invalid("The discount price cannot be negative and must have at most two decimal places.");
        }

        if (input.DiscountPrice == 0)
        {
            input.DiscountPrice = null;
        }

        if (input.DiscountPrice is { } discount && discount >= input.NormalPrice)
        {
            return AdminResult.Invalid("The discount price must be lower than the normal price.");
        }

        if (!string.IsNullOrWhiteSpace(input.ImageUrl) &&
            (input.ImageUrl.Length > 2048 ||
             !(Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo))))
        {
            return AdminResult.Invalid("The image URL must be an absolute https address without embedded credentials.");
        }

        if (input.ImageUpload is not null &&
            (input.RemoveImage || !string.IsNullOrWhiteSpace(input.ImageUrl)))
        {
            return AdminResult.Invalid("Choose only one image action: upload, external URL, or remove.");
        }

        if (input.StockQuantity is < 0 or > 1_000_000)
        {
            return AdminResult.Invalid("Stock must be between 0 and 1,000,000.");
        }

        return null;
    }

    private static void Apply(Product product, AdminProductInput input)
    {
        product.Name = input.Name.Trim();
        product.CategoryId = input.CategoryId;
        product.ShortDescription = input.ShortDescription.Trim();
        product.FullDescription = input.FullDescription.Trim();
        product.NormalPrice = Money.Round(input.NormalPrice);
        product.DiscountPrice = input.DiscountPrice is { } discount ? Money.Round(discount) : null;
        product.StockQuantity = input.StockQuantity;
        product.IsFeatured = input.IsFeatured;
        product.IsActive = input.IsActive;
    }

    private async Task<ImageSelection> SelectImageAsync(
        AdminProductInput input,
        ProductImageKind existingKind,
        string? existingLocation,
        bool isCreate,
        CancellationToken cancellationToken)
    {
        if (input.ImageUpload is not null)
        {
            var saved = await images.ValidateAndSaveAsync(input.ImageUpload, cancellationToken);
            return saved.Succeeded
                ? new ImageSelection(ProductImageKind.Local, saved.RelativePath, true, null)
                : new ImageSelection(existingKind, existingLocation, false, saved.Error ?? "The image could not be saved.");
        }

        if (input.RemoveImage)
        {
            return new ImageSelection(ProductImageKind.None, null, false, null);
        }

        if (!string.IsNullOrWhiteSpace(input.ImageUrl))
        {
            return new ImageSelection(ProductImageKind.ExternalUrl, input.ImageUrl.Trim(), false, null);
        }

        return isCreate
            ? new ImageSelection(ProductImageKind.None, null, false, null)
            : new ImageSelection(existingKind, existingLocation, false, null);
    }

    private async Task CleanupNewImageAsync(ImageSelection selection, CancellationToken cancellationToken)
    {
        if (selection.IsNewManaged && selection.Location is not null)
        {
            await images.TryDeleteAsync(selection.Location, cancellationToken);
        }
    }

    private async Task<bool> CleanupOldImageAsync(
        int productId,
        ProductImageKind oldKind,
        string? oldLocation,
        ImageSelection replacement,
        CancellationToken cancellationToken)
    {
        if (oldKind != ProductImageKind.Local || string.IsNullOrWhiteSpace(oldLocation) ||
            string.Equals(oldLocation, replacement.Location, StringComparison.Ordinal))
        {
            return true;
        }

        var stillReferenced = await db.Products.AsNoTracking()
            .AnyAsync(product => product.Id != productId && product.ImageKind == ProductImageKind.Local && product.ImageLocation == oldLocation, cancellationToken);
        return stillReferenced || await images.TryDeleteAsync(oldLocation, cancellationToken);
    }

    private sealed record ImageSelection(ProductImageKind Kind, string? Location, bool IsNewManaged, string? Error);

    private async Task<string> GenerateSlugAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var suffix = 2;
        while (await db.Products.AnyAsync(product => product.Slug == slug && product.Id != excludeId, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        var previousDash = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? $"product-{Guid.NewGuid():N}"[..12] : slug;
    }
}
