using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;

namespace ECommerceStore.Web.Models.Orders;

public enum CheckoutAttemptStatus
{
    Processing = 1,
    Failed = 2,
    Succeeded = 3,
    Abandoned = 4
}

public enum OrderStatus
{
    Pending = 1,
    Paid = 2,
    Processing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6
}

public enum PaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed class CheckoutAttempt
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid Token { get; set; }
    public CheckoutAttemptStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int Version { get; set; } = 1;
    public ApplicationUser User { get; set; } = null!;
    public Order? Order { get; set; }
    public PaymentRecord? PaymentRecord { get; set; }
}

public sealed class Order
{
    public long Id { get; set; }
    public long CheckoutAttemptId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CustomerFirstName { get; set; } = string.Empty;
    public string CustomerLastName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerEmailNormalized { get; set; } = string.Empty;
    public string ShippingAddressLine1 { get; set; } = string.Empty;
    public string? ShippingAddressLine2 { get; set; }
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingPostalCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;
    public string ShippingPhoneNumber { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "USD";
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public int Version { get; set; } = 1;
    public CheckoutAttempt CheckoutAttempt { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public PaymentRecord? PaymentRecord { get; set; }
}

public sealed class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ListUnitPrice { get; set; }
    public decimal PaidUnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public Order Order { get; set; } = null!;
    public Product? Product { get; set; }
}

public sealed class PaymentRecord
{
    public long Id { get; set; }
    public long CheckoutAttemptId { get; set; }
    public long? OrderId { get; set; }
    public PaymentStatus Status { get; set; }
    public string Provider { get; set; } = "Fake";
    public string ProviderReference { get; set; } = string.Empty;
    public string ResultCode { get; set; } = string.Empty;
    public string? ResultMessage { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public CheckoutAttempt CheckoutAttempt { get; set; } = null!;
    public Order? Order { get; set; }
}
