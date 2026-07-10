using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Areas.Admin.Controllers;

public sealed class OrdersController(IAdminOrderService orders) : AdminControllerBase
{
    public async Task<IActionResult> Index([FromQuery] AdminOrderQuery query, CancellationToken cancellationToken) =>
        View(await orders.GetListAsync(query, cancellationToken));

    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var details = await orders.GetDetailsAsync(id, cancellationToken);
        return details is null ? NotFound() : View(details);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(AdminOrderStatusInput input, CancellationToken cancellationToken)
    {
        SetFeedback(await orders.UpdateStatusAsync(input, cancellationToken));
        return RedirectToAction(nameof(Details), new { id = input.OrderId });
    }
}
