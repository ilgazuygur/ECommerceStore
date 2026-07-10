using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.ViewModels.Cart;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Controllers;

[Route("cart")]
public sealed class CartController(ICartService cartService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await cartService.GetAsync(User, cancellationToken));

    [HttpPost("add")]
    public async Task<IActionResult> Add(AddCartItemInput input, string? returnUrl, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await cartService.AddAsync(User, input.ProductId, input.Quantity, cancellationToken)
            : CartOperationResult.Failure("Choose a valid quantity.");
        SetFeedback(result);
        return RedirectToLocal(returnUrl);
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update(UpdateCartItemInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await cartService.SetQuantityAsync(User, input.ProductId, input.Quantity, cancellationToken)
            : CartOperationResult.Failure("Choose a valid quantity.");
        SetFeedback(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remove")]
    public async Task<IActionResult> Remove([FromForm] int productId, CancellationToken cancellationToken)
    {
        SetFeedback(await cartService.RemoveAsync(User, productId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("increment")]
    public async Task<IActionResult> Increment([FromForm] int productId, CancellationToken cancellationToken)
    {
        SetFeedback(await cartService.AdjustQuantityAsync(User, productId, 1, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("decrement")]
    public async Task<IActionResult> Decrement([FromForm] int productId, CancellationToken cancellationToken)
    {
        SetFeedback(await cartService.AdjustQuantityAsync(User, productId, -1, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await cartService.ClearAsync(User, cancellationToken);
        TempData["Success"] = "Your cart is empty.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private void SetFeedback(CartOperationResult result) => TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
}
