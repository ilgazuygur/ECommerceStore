using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ECommerceStore.Web.Models;
using ECommerceStore.Web.Services.Catalog;

namespace ECommerceStore.Web.Controllers;

public class HomeController(IProductQueryService products) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await products.GetHomeAsync(cancellationToken));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Route("status/{code:int}")]
    [ActionName("StatusCode")]
    public IActionResult HandleStatusCode(int code)
    {
        Response.StatusCode = code;
        return code == StatusCodes.Status404NotFound ? View("NotFound") : View("StatusCode", code);
    }
}
