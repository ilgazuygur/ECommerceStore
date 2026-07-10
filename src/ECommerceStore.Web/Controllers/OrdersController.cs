using ECommerceStore.Web.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Controllers;

[Authorize, Route("orders")]
public sealed class OrdersController(ICheckoutService checkoutService) : Controller
{
    [HttpGet("confirmation/{orderNumber}")]
    public async Task<IActionResult> Confirmation(string orderNumber, CancellationToken cancellationToken)
    {
        // Ownership is enforced in the service query (order number + current user id). A miss returns 404
        // to avoid disclosing whether the order exists for another customer.
        var confirmation = await checkoutService.GetConfirmationAsync(User, orderNumber, cancellationToken);
        return confirmation is null ? NotFound() : View(confirmation);
    }
}
