using System.Text;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Admin;

public interface IAdminCategoryService
{
    Task<AdminCategoryListViewModel> GetListAsync(int page, CancellationToken cancellationToken = default);
    Task<AdminCategoryInput?> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<AdminResult> CreateAsync(AdminCategoryInput input, CancellationToken cancellationToken = default);
    Task<AdminResult> UpdateAsync(AdminCategoryInput input, CancellationToken cancellationToken = default);
    Task<AdminResult> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);
    Task<AdminResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class AdminCategoryService(ApplicationDbContext db, IOptions<StoreOptions> storeOptions) : IAdminCategoryService
{
    private readonly int _pageSize = storeOptions.Value.AdminPageSize;

    public async Task<AdminCategoryListViewModel> GetListAsync(int page, CancellationToken cancellationToken = default)
    {
        var currentPage = page < 1 ? 1 : page;
        var total = await db.Categories.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)_pageSize));
        currentPage = Math.Min(currentPage, totalPages);

        var items = await db.Categories.AsNoTracking()
            .OrderBy(category => category.Name)
            .Skip((currentPage - 1) * _pageSize).Take(_pageSize)
            .Select(category => new AdminCategoryListItem(
                category.Id, category.Name, category.Slug, category.IsActive, category.Products.Count))
            .ToListAsync(cancellationToken);

        return new AdminCategoryListViewModel { Categories = items, Page = currentPage, PageSize = _pageSize, TotalCount = total };
    }

    public async Task<AdminCategoryInput?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return category is null ? null : new AdminCategoryInput { Id = category.Id, Name = category.Name, IsActive = category.IsActive, Version = category.Version };
    }

    public async Task<AdminResult> CreateAsync(AdminCategoryInput input, CancellationToken cancellationToken = default)
    {
        var name = input.Name.Trim();
        if (name.Length is 0 or > 100)
        {
            return AdminResult.Invalid("Category name is required and cannot exceed 100 characters.");
        }

        var normalized = name.ToUpperInvariant();
        var slug = await GenerateSlugAsync(name, null, cancellationToken);

        if (await db.Categories.AnyAsync(category => category.NormalizedName == normalized, cancellationToken))
        {
            return AdminResult.Conflict("A category with that name already exists.");
        }

        db.Categories.Add(new Category { Name = name, NormalizedName = normalized, Slug = slug, IsActive = input.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return AdminResult.Ok("Category created.");
    }

    public async Task<AdminResult> UpdateAsync(AdminCategoryInput input, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(candidate => candidate.Id == input.Id, cancellationToken);
        if (category is null)
        {
            return AdminResult.NotFound();
        }

        var name = input.Name.Trim();
        if (name.Length is 0 or > 100)
        {
            return AdminResult.Invalid("Category name is required and cannot exceed 100 characters.");
        }

        var normalized = name.ToUpperInvariant();
        if (await db.Categories.AnyAsync(other => other.NormalizedName == normalized && other.Id != category.Id, cancellationToken))
        {
            return AdminResult.Conflict("A category with that name already exists.");
        }

        if (category.Name != name)
        {
            category.Slug = await GenerateSlugAsync(name, category.Id, cancellationToken);
        }

        category.Name = name;
        category.NormalizedName = normalized;
        category.IsActive = input.IsActive;
        db.Entry(category).Property(candidate => candidate.Version).OriginalValue = input.Version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AdminResult.Ok("Category updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminResult.Conflict("This category was changed by someone else. Reload and try again.");
        }
    }

    public async Task<AdminResult> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (category is null)
        {
            return AdminResult.NotFound();
        }

        category.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return AdminResult.Ok(active ? "Category activated." : "Category deactivated.");
    }

    public async Task<AdminResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (category is null)
        {
            return AdminResult.NotFound();
        }

        // A category referenced by any product cannot be deleted (products must always have a valid
        // category); deactivate instead so it disappears from the storefront without orphaning products.
        if (await db.Products.AnyAsync(product => product.CategoryId == id, cancellationToken))
        {
            if (!category.IsActive)
            {
                return AdminResult.Blocked("This category still has products and is already deactivated, so it cannot be deleted.");
            }

            category.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
            return AdminResult.Blocked("This category still has products, so it was deactivated instead of deleted.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return AdminResult.Ok("Category deleted.");
    }

    private async Task<string> GenerateSlugAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var suffix = 2;
        while (await db.Categories.AnyAsync(category => category.Slug == slug && category.Id != excludeId, cancellationToken))
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
        return string.IsNullOrEmpty(slug) ? $"category-{Guid.NewGuid():N}"[..13] : slug;
    }
}
