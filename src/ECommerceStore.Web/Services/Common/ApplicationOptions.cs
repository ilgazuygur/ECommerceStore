using System.ComponentModel.DataAnnotations;

namespace ECommerceStore.Web.Services.Common;

public sealed class StoreOptions
{
    public const string SectionName = "Store";
    [Required, StringLength(100)] public string Name { get; set; } = "ECommerceStore";
    [RegularExpression("^[A-Z]{3}$")] public string CurrencyCode { get; set; } = "USD";
    [Range(0, 1_000_000)] public decimal ShippingFee { get; set; } = 10m;
    [Range(0, 1_000_000)] public int LowStockThreshold { get; set; } = 5;
    [Range(1, 48)] public int CatalogPageSize { get; set; } = 12;
    [Range(1, 100)] public int AdminPageSize { get; set; } = 20;
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = string.Empty;
    [Range(1, 65535)] public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    [EmailAddress] public string FromAddress { get; set; } = "noreply@localstore.test";
    public string FromName { get; set; } = "ECommerceStore";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";
    [Range(1, 10 * 1024 * 1024)] public long MaximumBytes { get; set; } = 5 * 1024 * 1024;
    [Required] public string RelativeRoot { get; set; } = "uploads/products";
    [Range(1, 10_000)] public int MaximumWidth { get; set; } = 10_000;
    [Range(1, 10_000)] public int MaximumHeight { get; set; } = 10_000;
    [Range(1, 40_000_000)] public long MaximumPixels { get; set; } = 40_000_000;
}

public sealed class AIServiceOptions
{
    public const string SectionName = "AI";
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "Mock";
}
