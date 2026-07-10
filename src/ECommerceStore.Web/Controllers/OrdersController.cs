using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Controllers;

[Authorize, Route("orders")]
public sealed class OrdersController(ICheckoutService checkoutService, IOrderQueryService orderQuery) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        View(await orderQuery.GetMyOrdersAsync(User, page, cancellationToken));

    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Details(string orderNumber, CancellationToken cancellationToken)
    {
        // Ownership is enforced in the service query (order number + current user id).
        var details = await orderQuery.GetOrderDetailsAsync(User, orderNumber, cancellationToken);
        return details is null ? NotFound() : View(details);
    }

    [HttpGet("confirmation/{orderNumber}")]
    public async Task<IActionResult> Confirmation(string orderNumber, CancellationToken cancellationToken)
    {
        // Ownership is enforced in the service query (order number + current user id). A miss returns 404
        // to avoid disclosing whether the order exists for another customer.
        var confirmation = await checkoutService.GetConfirmationAsync(User, orderNumber, cancellationToken);
        return confirmation is null ? NotFound() : View(confirmation);
    }
}
