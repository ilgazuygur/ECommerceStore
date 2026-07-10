using System.ComponentModel.DataAnnotations;

namespace ECommerceStore.Web.ViewModels.Admin;

public sealed class DashboardViewModel
{
    public int TotalProducts { get; init; }
    public int ActiveProducts { get; init; }
    public int TotalCustomers { get; init; }
    public int TotalOrders { get; init; }
    public int PendingOrders { get; init; }
    public int CompletedOrders { get; init; }
    public decimal Revenue { get; init; }
    public int LowStockThreshold { get; init; }
    public IReadOnlyList<LowStockItem> LowStockProducts { get; init; } = [];
}

public sealed record LowStockItem(int Id, string Name, int StockQuantity, bool IsActive);

public abstract class PagedViewModel
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

// ---- Products ----

public sealed class AdminProductQuery
{
    [StringLength(100)] public string? Keyword { get; set; }
    public int? CategoryId { get; set; }
    public bool? ActiveOnly { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
}

public sealed record AdminProductListItem(
    int Id, string Name, string CategoryName, decimal NormalPrice, decimal? DiscountPrice,
    int StockQuantity, bool IsActive, bool IsFeatured);

public sealed class AdminProductListViewModel : PagedViewModel
{
    public required AdminProductQuery Query { get; init; }
    public required IReadOnlyList<AdminProductListItem> Products { get; init; }
    public required IReadOnlyList<CategoryOption> Categories { get; init; }
}

public sealed record CategoryOption(int Id, string Name, bool IsActive);

public sealed class AdminProductInput
{
    public int? Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public int CategoryId { get; set; }
    [Required, StringLength(500)] public string ShortDescription { get; set; } = string.Empty;
    [Required, StringLength(8000)] public string FullDescription { get; set; } = string.Empty;
    [Range(0.01, 9_999_999.99)] public decimal NormalPrice { get; set; }
    [Range(0.00, 9_999_999.99)] public decimal? DiscountPrice { get; set; }
    [Range(0, 1_000_000)] public int StockQuantity { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(2048), Url] public string? ImageUrl { get; set; }
    public int Version { get; set; }
}

// ---- Categories ----

public sealed record AdminCategoryListItem(int Id, string Name, string Slug, bool IsActive, int ProductCount);

public sealed class AdminCategoryListViewModel : PagedViewModel
{
    public required IReadOnlyList<AdminCategoryListItem> Categories { get; init; }
}

public sealed class AdminCategoryInput
{
    public int? Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
}

// ---- Orders ----

public sealed class AdminOrderQuery
{
    [StringLength(64)] public string? Search { get; set; }
    public string? Status { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
}

public sealed record AdminOrderListItem(
    long OrderId, string OrderNumber, string CustomerEmail, DateTime CreatedAtUtc, decimal GrandTotal,
    string OrderStatus, string PaymentStatus, int ItemCount);

public sealed class AdminOrderListViewModel : PagedViewModel
{
    public required AdminOrderQuery Query { get; init; }
    public required IReadOnlyList<AdminOrderListItem> Orders { get; init; }
    public required IReadOnlyList<string> StatusOptions { get; init; }
}

public sealed record AdminOrderLine(string ProductName, int Quantity, decimal PaidUnitPrice, decimal LineTotal);

public sealed class AdminOrderDetailsViewModel
{
    public long OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string OrderStatus { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Shipping { get; init; }
    public decimal GrandTotal { get; init; }
    public int Version { get; init; }
    public IReadOnlyList<AdminOrderLine> Lines { get; init; } = [];
    public IReadOnlyList<string> AllowedTransitions { get; init; } = [];
}

public sealed class AdminOrderStatusInput
{
    [Required] public long OrderId { get; set; }
    [Required] public string NewStatus { get; set; } = string.Empty;
    public int Version { get; set; }
}

// ---- Customers ----

public sealed class AdminCustomerQuery
{
    [StringLength(256)] public string? Search { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
}

public sealed record AdminCustomerListItem(
    string Id, string FullName, string Email, string? PhoneNumber, bool IsEnabled, int OrderCount, DateTime CreatedAtUtc);

public sealed class AdminCustomerListViewModel : PagedViewModel
{
    public required AdminCustomerQuery Query { get; init; }
    public required IReadOnlyList<AdminCustomerListItem> Customers { get; init; }
}

public sealed record AdminCustomerOrderSummary(string OrderNumber, DateTime CreatedAtUtc, decimal GrandTotal, string OrderStatus);

public sealed class AdminCustomerDetailsViewModel
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsAdministrator { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public IReadOnlyList<AdminCustomerOrderSummary> Orders { get; init; } = [];
}
