namespace ECommerceStore.Web.ViewModels.Orders;

public sealed class OrderListItem
{
    public string OrderNumber { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public decimal GrandTotal { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}

public sealed class OrderListViewModel
{
    public IReadOnlyList<OrderListItem> Orders { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
    public bool IsEmpty => Orders.Count == 0;
}

public sealed class OrderDetailLine
{
    public string ProductName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal ListUnitPrice { get; init; }
    public decimal PaidUnitPrice { get; init; }
    public decimal LineDiscount { get; init; }
    public decimal LineTotal { get; init; }
    public bool IsDiscounted => PaidUnitPrice < ListUnitPrice;
}

public sealed class OrderDetailsViewModel
{
    public string OrderNumber { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string? CardBrand { get; init; }
    public string? CardLast4 { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Shipping { get; init; }
    public decimal GrandTotal { get; init; }
    public IReadOnlyList<OrderDetailLine> Lines { get; init; } = [];
}
