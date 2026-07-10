using ECommerceStore.Web.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Areas.Admin.Controllers;

public sealed class DashboardController(IAdminDashboardService dashboard) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await dashboard.GetAsync(cancellationToken));
}
