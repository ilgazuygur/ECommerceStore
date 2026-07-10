using System.Security.Claims;
using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Areas.Admin.Controllers;

public sealed class CustomersController(IAdminCustomerService customers) : AdminControllerBase
{
    public async Task<IActionResult> Index([FromQuery] AdminCustomerQuery query, CancellationToken cancellationToken) =>
        View(await customers.GetListAsync(query, cancellationToken));

    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        var details = await customers.GetDetailsAsync(id, cancellationToken);
        return details is null ? NotFound() : View(details);
    }

    [HttpPost]
    public async Task<IActionResult> Disable(string id, CancellationToken cancellationToken)
    {
        SetFeedback(await customers.SetEnabledAsync(id, enabled: false, CurrentUserId(), cancellationToken));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Enable(string id, CancellationToken cancellationToken)
    {
        SetFeedback(await customers.SetEnabledAsync(id, enabled: true, CurrentUserId(), cancellationToken));
        return RedirectToAction(nameof(Details), new { id });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
