using System.Security.Cryptography;
using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Payments;
using ECommerceStore.Web.ViewModels.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Controllers;

[Authorize, Route("checkout")]
public sealed class CheckoutController(ICheckoutService checkoutService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cart = await checkoutService.GetCartAsync(User, cancellationToken);
        if (cart.IsEmpty)
        {
            TempData["Warning"] = "Your cart is empty, so there is nothing to check out.";
            return RedirectToAction("Index", "Cart");
        }

        return View(new CheckoutViewModel { Cart = cart, Input = new CheckoutInputModel { Token = NewToken() } });
    }

    [HttpPost("")]
    public async Task<IActionResult> Index(CheckoutInputModel input, CancellationToken cancellationToken)
    {
        var cart = await checkoutService.GetCartAsync(User, cancellationToken);
        if (cart.IsEmpty)
        {
            TempData["Warning"] = "Your cart is empty, so there is nothing to check out.";
            return RedirectToAction("Index", "Cart");
        }

        if (!ModelState.IsValid)
        {
            // Redisplay with the SAME token: no attempt was claimed, so a retry stays idempotent.
            return Redisplay(input, cart, keepToken: true);
        }

        var command = new PlaceOrderCommand(
            input.Token,
            input.AddressLine1.Trim(),
            input.AddressLine2?.Trim(),
            input.City.Trim(),
            input.PostalCode.Trim(),
            input.Country.Trim(),
            input.PhoneNumber.Trim(),
            new FakePaymentRequest(input.CardNumber, input.CardHolder.Trim(), input.ExpiryMonth, input.ExpiryYear, input.SecurityCode));

        var result = await checkoutService.PlaceOrderAsync(User, command, cancellationToken);
        switch (result.Outcome)
        {
            case CheckoutOutcome.Success:
                TempData["Success"] = "Payment approved. Your order is confirmed.";
                return RedirectToAction("Confirmation", "Orders", new { orderNumber = result.OrderNumber });
            case CheckoutOutcome.AlreadyPlaced:
                TempData["Warning"] = "This order was already placed.";
                return RedirectToAction("Confirmation", "Orders", new { orderNumber = result.OrderNumber });
            case CheckoutOutcome.EmptyCart:
                TempData["Warning"] = result.Message;
                return RedirectToAction("Index", "Cart");
            case CheckoutOutcome.Processing:
                TempData["Warning"] = result.Message;
                return RedirectToAction("Index", "Cart");
            case CheckoutOutcome.Declined:
            case CheckoutOutcome.StockConflict:
            default:
                ModelState.AddModelError(string.Empty, result.Message ?? "We could not complete your order.");
                // A new token so the customer can retry cleanly after a declined/consumed attempt.
                return Redisplay(input, cart, keepToken: false);
        }
    }

    private IActionResult Redisplay(CheckoutInputModel input, ViewModels.Cart.CartViewModel cart, bool keepToken)
    {
        ClearCardState(input);
        if (!keepToken)
        {
            // A declined/consumed attempt needs a fresh token so a corrected retry starts clean.
            input.Token = NewToken();
            ModelState.Remove($"{nameof(CheckoutViewModel.Input)}.{nameof(CheckoutInputModel.Token)}");
            ModelState.Remove(nameof(CheckoutInputModel.Token));
        }

        return View(new CheckoutViewModel { Cart = cart, Input = input });
    }

    private void ClearCardState(CheckoutInputModel input)
    {
        // PAY-002: never redisplay or retain card secrets. Wipe the model values and fully remove the
        // model-state entries so the tag helper cannot re-emit the posted AttemptedValue.
        input.CardNumber = string.Empty;
        input.SecurityCode = string.Empty;
        input.CardHolder = string.Empty;
        input.ExpiryMonth = 0;
        input.ExpiryYear = 0;
        foreach (var property in new[]
        {
            nameof(CheckoutInputModel.CardNumber),
            nameof(CheckoutInputModel.SecurityCode),
            nameof(CheckoutInputModel.CardHolder),
            nameof(CheckoutInputModel.ExpiryMonth),
            nameof(CheckoutInputModel.ExpiryYear)
        })
        {
            ModelState.Remove($"{nameof(CheckoutViewModel.Input)}.{property}");
            ModelState.Remove(property);
        }
    }

    private static Guid NewToken()
    {
        // Cryptographically random, user-bound idempotency token.
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return new Guid(bytes);
    }
}
