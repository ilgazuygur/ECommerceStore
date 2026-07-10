using Microsoft.AspNetCore.Identity;

namespace ECommerceStore.Web.Models.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
