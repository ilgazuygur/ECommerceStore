using ECommerceStore.Web.Services.Catalog;
using ECommerceStore.Web.ViewModels.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Controllers;

[Route("products")]
public sealed class CatalogController(IProductQueryService products) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ProductQueryRequest request, CancellationToken cancellationToken) =>
        View(await products.SearchAsync(request, cancellationToken));

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
    {
        var product = await products.GetDetailsAsync(slug, cancellationToken);
        return product is null ? NotFound() : View(product);
    }

    [HttpGet("/category/{slug}")]
    public IActionResult Category(string slug) => RedirectToAction(nameof(Index), new { category = slug });
}
