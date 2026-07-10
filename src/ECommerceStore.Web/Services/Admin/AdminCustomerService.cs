using ECommerceStore.Web.Data;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Admin;

public interface IAdminCustomerService
{
    Task<AdminCustomerListViewModel> GetListAsync(AdminCustomerQuery query, CancellationToken cancellationToken = default);
    Task<AdminCustomerDetailsViewModel?> GetDetailsAsync(string userId, CancellationToken cancellationToken = default);
    Task<AdminResult> SetEnabledAsync(string userId, bool enabled, string currentAdminId, CancellationToken cancellationToken = default);
}

public sealed class AdminCustomerService(ApplicationDbContext db, IOptions<StoreOptions> storeOptions) : IAdminCustomerService
{
    private readonly int _pageSize = storeOptions.Value.AdminPageSize;

    public async Task<AdminCustomerListViewModel> GetListAsync(AdminCustomerQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var users = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(user =>
                EF.Functions.Like(user.Email!, $"%{search}%") ||
                EF.Functions.Like(user.FirstName, $"%{search}%") ||
                EF.Functions.Like(user.LastName, $"%{search}%"));
        }

        var total = await users.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)_pageSize));
        page = Math.Min(page, totalPages);

        // Project only allow-listed profile fields — never password hashes, security stamps, or other
        // Identity internals.
        var items = await users
            .OrderBy(user => user.Email)
            .Skip((page - 1) * _pageSize).Take(_pageSize)
            .Select(user => new AdminCustomerListItem(
                user.Id,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Email!,
                user.PhoneNumber,
                user.IsEnabled,
                db.Orders.Count(order => order.UserId == user.Id),
                user.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new AdminCustomerListViewModel { Query = query, Customers = items, Page = page, PageSize = _pageSize, TotalCount = total };
    }

    public async Task<AdminCustomerDetailsViewModel?> GetDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Id, candidate.FirstName, candidate.LastName, candidate.Email, candidate.PhoneNumber, candidate.IsEnabled, candidate.CreatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var orders = await db.Orders.AsNoTracking()
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .Select(order => new AdminCustomerOrderSummary(order.OrderNumber, order.CreatedAtUtc, order.GrandTotal, order.OrderStatus.ToString()))
            .ToListAsync(cancellationToken);

        return new AdminCustomerDetailsViewModel
        {
            Id = user.Id,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            Email = user.Email!,
            PhoneNumber = user.PhoneNumber,
            IsEnabled = user.IsEnabled,
            IsAdministrator = await IsAdministratorAsync(userId, cancellationToken),
            CreatedAtUtc = user.CreatedAtUtc,
            Orders = orders
        };
    }

    public async Task<AdminResult> SetEnabledAsync(string userId, bool enabled, string currentAdminId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return AdminResult.NotFound("That customer no longer exists.");
        }

        if (!enabled)
        {
            if (userId == currentAdminId)
            {
                return AdminResult.Blocked("You cannot disable your own account.");
            }

            if (await IsAdministratorAsync(userId, cancellationToken) && await EnabledAdministratorCountAsync(cancellationToken) <= 1)
            {
                return AdminResult.Blocked("You cannot disable the last enabled administrator.");
            }
        }

        user.IsEnabled = enabled;
        // Rotate this Identity-managed value without projecting it into any view. The custom cookie
        // validator rejects disabled users on every request; rotation also invalidates any security-
        // stamp-validated sessions and ensures re-enable never revives an old authentication cookie.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(cancellationToken);
        return AdminResult.Ok(enabled ? "Account re-enabled." : "Account disabled.");
    }

    private async Task<bool> IsAdministratorAsync(string userId, CancellationToken cancellationToken)
    {
        var adminRoleId = await db.Roles.Where(role => role.Name == RoleNames.Administrator).Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        return adminRoleId is not null && await db.UserRoles.AnyAsync(link => link.UserId == userId && link.RoleId == adminRoleId, cancellationToken);
    }

    private async Task<int> EnabledAdministratorCountAsync(CancellationToken cancellationToken)
    {
        var adminRoleId = await db.Roles.Where(role => role.Name == RoleNames.Administrator).Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        if (adminRoleId is null)
        {
            return 0;
        }

        return await db.UserRoles
            .Where(link => link.RoleId == adminRoleId)
            .Join(db.Users, link => link.UserId, user => user.Id, (link, user) => user)
            .CountAsync(user => user.IsEnabled, cancellationToken);
    }
}
