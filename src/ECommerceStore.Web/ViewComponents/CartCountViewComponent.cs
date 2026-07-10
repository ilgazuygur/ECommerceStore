using ECommerceStore.Web.Services.Cart;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.ViewComponents;

public sealed class CartCountViewComponent(ICartService cartService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await cartService.GetCountAsync(UserClaimsPrincipal, HttpContext.RequestAborted));
}
