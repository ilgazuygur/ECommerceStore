using System.ComponentModel.DataAnnotations;

namespace ECommerceStore.Web.ViewModels.Cart;

public sealed record CartPriceLine(int ProductId, int Quantity, decimal ListUnitPrice, decimal PaidUnitPrice);

public sealed record CartTotals(
    decimal ListPriceSubtotal,
    decimal DiscountTotal,
    decimal MerchandiseTotal,
    decimal Shipping,
    decimal GrandTotal);

public sealed class CartLineViewModel
{
    public int ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public int Quantity { get; init; }
    public int MaximumQuantity { get; init; }
    public decimal ListUnitPrice { get; init; }
    public decimal PaidUnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public decimal LineDiscount { get; init; }
    public bool IsDiscounted => PaidUnitPrice < ListUnitPrice;
}

public sealed class CartViewModel
{
    public IReadOnlyList<CartLineViewModel> Lines { get; init; } = [];
    public CartTotals Totals { get; init; } = new(0, 0, 0, 0, 0);
    public int TotalUnits { get; init; }
    public bool IsAuthenticated { get; init; }
    public bool IsEmpty => Lines.Count == 0;
}

public sealed class AddCartItemInput
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 1_000_000)] public int Quantity { get; set; } = 1;
}

public sealed class UpdateCartItemInput
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 1_000_000)] public int Quantity { get; set; }
}

public sealed record CartOperationResult(bool Succeeded, string Message)
{
    public static CartOperationResult Success(string message) => new(true, message);
    public static CartOperationResult Failure(string message) => new(false, message);
}

public sealed record CartMergeResult(bool Succeeded, IReadOnlyList<string> Warnings)
{
    public static CartMergeResult Empty { get; } = new(true, []);
}
