using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Email;
using ECommerceStore.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoreConfiguration = ECommerceStore.Web.Services.Common.StoreOptions;

namespace ECommerceStore.Web.Controllers;

[Route("account")]
public sealed class AccountController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ICartMergeService cartMerge,
    IEmailComposer emailComposer,
    IEmailService emailService,
    IOptions<StoreConfiguration> storeOptions,
    ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous, HttpGet("register")]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new RegisterViewModel { ReturnUrl = LocalOrNull(returnUrl) });
    }

    [AllowAnonymous, HttpPost("register")]
    public async Task<IActionResult> Register(RegisterViewModel input, CancellationToken cancellationToken)
    {
        input.ReturnUrl = LocalOrNull(input.ReturnUrl);
        if (!ModelState.IsValid)
        {
            ClearPasswords(input);
            return View(input);
        }

        var email = input.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim(),
            IsEnabled = true
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var createResult = await userManager.CreateAsync(user, input.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            AddIdentityErrors(createResult);
            ClearPasswords(input);
            return View(input);
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Customer);
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError("Customer role assignment failed during registration; the user transaction was rolled back.");
            ModelState.AddModelError(string.Empty, "We could not finish creating your account. Please try again.");
            ClearPasswords(input);
            return View(input);
        }

        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, isPersistent: false);
        await MergeCartAsync(user.Id, cancellationToken);

        var message = emailComposer.ComposeWelcome(user.Email!, new WelcomeEmailModel(user.FirstName, storeOptions.Value.Name));
        _ = await emailService.SendAsync(message, cancellationToken);
        TempData["Success"] = "Welcome! Your account is ready.";
        return RedirectToLocal(input.ReturnUrl);
    }

    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = LocalOrNull(returnUrl) });
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginViewModel input, CancellationToken cancellationToken)
    {
        input.ReturnUrl = LocalOrNull(input.ReturnUrl);
        if (!ModelState.IsValid)
        {
            ClearPassword(input);
            return View(input);
        }
        var user = await userManager.FindByEmailAsync(input.Email.Trim());
        if (user is null || !user.IsEnabled)
        {
            AddLoginFailure();
            ClearPassword(input);
            return View(input);
        }

        var check = await signInManager.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            AddLoginFailure();
            ClearPassword(input);
            return View(input);
        }

        await signInManager.SignInAsync(user, input.RememberMe);
        await MergeCartAsync(user.Id, cancellationToken);
        TempData["Success"] = $"Welcome back, {user.FirstName}.";
        return RedirectToLocal(input.ReturnUrl);
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        TempData["Success"] = "You have signed out.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet("access-denied")]
    public IActionResult AccessDenied() => View();

    private async Task MergeCartAsync(string userId, CancellationToken cancellationToken)
    {
        var result = await cartMerge.MergeAsync(userId, cancellationToken);
        if (result.Warnings.Count > 0) TempData[result.Succeeded ? "Warning" : "Error"] = string.Join(" ", result.Warnings);
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var message = error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
                ? "An account with that email already exists."
                : error.Description;
            ModelState.AddModelError(string.Empty, message);
        }
    }

    private void AddLoginFailure() => ModelState.AddModelError(string.Empty, "The email or password is incorrect.");
    private void ClearPasswords(RegisterViewModel input)
    {
        input.Password = string.Empty;
        input.ConfirmPassword = string.Empty;
        ModelState.SetModelValue(nameof(input.Password), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(string.Empty));
        ModelState.SetModelValue(nameof(input.ConfirmPassword), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(string.Empty));
    }
    private void ClearPassword(LoginViewModel input)
    {
        input.Password = string.Empty;
        ModelState.SetModelValue(nameof(input.Password), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(string.Empty));
    }
    private string? LocalOrNull(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
    private IActionResult RedirectToLocal(string? returnUrl) => returnUrl is not null ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home")!;
}
