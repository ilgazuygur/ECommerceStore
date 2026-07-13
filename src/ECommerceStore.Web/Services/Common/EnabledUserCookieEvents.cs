using ECommerceStore.Web.Models.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ECommerceStore.Web.Services.Common;

public sealed class EnabledUserCookieEvents(UserManager<ApplicationUser> userManager) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var user = await userManager.GetUserAsync(context.Principal!);
        if (user is null || !user.IsEnabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        await base.ValidatePrincipal(context);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        IsAssistantApi(context.Request) ? WriteProblemAsync(context.Response, StatusCodes.Status401Unauthorized, "Authentication required") : base.RedirectToLogin(context);

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        IsAssistantApi(context.Request) ? WriteProblemAsync(context.Response, StatusCodes.Status403Forbidden, "Access denied") : base.RedirectToAccessDenied(context);

    private static bool IsAssistantApi(HttpRequest request) => request.Path.StartsWithSegments("/api/assistant", StringComparison.OrdinalIgnoreCase);

    private static Task WriteProblemAsync(HttpResponse response, int status, string title)
    {
        response.StatusCode = status;
        response.ContentType = "application/problem+json";
        return response.WriteAsync(JsonSerializer.Serialize(new ProblemDetails { Status = status, Title = title }));
    }
}
