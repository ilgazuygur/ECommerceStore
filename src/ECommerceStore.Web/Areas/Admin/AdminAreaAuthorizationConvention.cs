using ECommerceStore.Web.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace ECommerceStore.Web.Areas.Admin;

/// <summary>
/// Applies an Administrator policy to every controller discovered in the Admin area. Controllers also
/// inherit the explicit role attribute from AdminControllerBase as defense in depth.
/// </summary>
public sealed class AdminAreaAuthorizationConvention : IControllerModelConvention
{
    private static readonly AuthorizeFilter AdministratorFilter = new(
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(RoleNames.Administrator).Build());

    public void Apply(ControllerModel controller)
    {
        var area = controller.Attributes.OfType<AreaAttribute>().FirstOrDefault()?.RouteValue;
        if (string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            controller.Filters.Add(AdministratorFilter);
        }
    }
}
