using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.Services.AI;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Areas.Admin.Controllers;

public sealed class ProductsController(IAdminProductService products, IAIService ai) : AdminControllerBase
{
    public async Task<IActionResult> Index([FromQuery] AdminProductQuery query, CancellationToken cancellationToken) =>
        View(await products.GetListAsync(query, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulateCategoriesAsync(cancellationToken);
        return View(new AdminProductInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(AdminProductInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(cancellationToken);
            return View(input);
        }

        var result = await products.CreateAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
            await PopulateCategoriesAsync(cancellationToken);
            return View(input);
        }

        SetFeedback(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var input = await products.GetForEditAsync(id, cancellationToken);
        if (input is null)
        {
            return NotFound();
        }

        await PopulateCategoriesAsync(cancellationToken);
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(AdminProductInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(cancellationToken);
            return View(input);
        }

        var result = await products.UpdateAsync(input, cancellationToken);
        if (result.Outcome is AdminOutcome.Invalid)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
            await PopulateCategoriesAsync(cancellationToken);
            return View(input);
        }

        SetFeedback(result);
        return result.Outcome is AdminOutcome.NotFound ? NotFound() : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        SetFeedback(await products.SetActiveAsync(id, true, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        SetFeedback(await products.SetActiveAsync(id, false, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        SetFeedback(await products.DeleteAsync(id, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    // Optional AI-002: returns editable, unsaved draft text. It never persists or overwrites anything.
    [HttpPost]
    public async Task<IActionResult> DraftDescription(string? name, string? category, string? keywords, CancellationToken cancellationToken)
    {
        if (!ai.IsEnabled)
        {
            return Json(new { available = false });
        }

        var result = await ai.DraftProductDescriptionAsync(new AIDraftRequest(name ?? string.Empty, category, keywords), cancellationToken);
        return Json(new { available = result.Available, draft = result.DraftText });
    }

    private async Task PopulateCategoriesAsync(CancellationToken cancellationToken)
    {
        var list = await products.GetCategoryOptionsAsync(cancellationToken);
        ViewBag.Categories = list;
        ViewBag.AiEnabled = ai.IsEnabled;
    }
}
