using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for every admin controller. The area-wide authorization is enforced centrally here (and again by
/// convention in Program.cs), so no admin action is reachable by an anonymous or customer request.
/// </summary>
[Area("Admin")]
[Authorize(Roles = RoleNames.Administrator)]
public abstract class AdminControllerBase : Controller
{
    protected void SetFeedback(AdminResult result)
    {
        var key = result.Outcome switch
        {
            AdminOutcome.Success => "Success",
            AdminOutcome.Blocked or AdminOutcome.Conflict => "Warning",
            _ => "Error"
        };
        TempData[key] = result.Message ?? result.Outcome.ToString();
    }
}
