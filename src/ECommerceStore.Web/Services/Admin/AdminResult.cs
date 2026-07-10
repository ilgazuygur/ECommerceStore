namespace ECommerceStore.Web.Services.Admin;

public enum AdminOutcome
{
    Success,
    NotFound,
    Conflict,
    Invalid,
    Blocked
}

/// <summary>Result of an admin mutation. <see cref="AdminOutcome.Blocked"/> means a safety rule refused the action.</summary>
public sealed record AdminResult(AdminOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome == AdminOutcome.Success;

    public static AdminResult Ok(string? message = null) => new(AdminOutcome.Success, message);
    public static AdminResult NotFound(string message = "That item no longer exists.") => new(AdminOutcome.NotFound, message);
    public static AdminResult Conflict(string message) => new(AdminOutcome.Conflict, message);
    public static AdminResult Invalid(string message) => new(AdminOutcome.Invalid, message);
    public static AdminResult Blocked(string message) => new(AdminOutcome.Blocked, message);
}
