using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Areas.Admin.Controllers;

public sealed class CategoriesController(IAdminCategoryService categories) : AdminControllerBase
{
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default) =>
        View(await categories.GetListAsync(page, cancellationToken));

    [HttpGet]
    public IActionResult Create() => View(new AdminCategoryInput());

    [HttpPost]
    public async Task<IActionResult> Create(AdminCategoryInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var result = await categories.CreateAsync(input, cancellationToken);
        if (result.Outcome is AdminOutcome.Conflict or AdminOutcome.Invalid)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
            return View(input);
        }

        SetFeedback(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var input = await categories.GetForEditAsync(id, cancellationToken);
        return input is null ? NotFound() : View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(AdminCategoryInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var result = await categories.UpdateAsync(input, cancellationToken);
        if (result.Outcome is AdminOutcome.Conflict or AdminOutcome.Invalid)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
            return View(input);
        }

        SetFeedback(result);
        return result.Outcome is AdminOutcome.NotFound ? NotFound() : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        SetFeedback(await categories.SetActiveAsync(id, true, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        SetFeedback(await categories.SetActiveAsync(id, false, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        SetFeedback(await categories.DeleteAsync(id, cancellationToken));
        return RedirectToAction(nameof(Index));
    }
}
