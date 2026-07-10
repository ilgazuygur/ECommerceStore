using System.ComponentModel.DataAnnotations;
using ECommerceStore.Web.ViewModels.Cart;

namespace ECommerceStore.Web.ViewModels.Checkout;

/// <summary>
/// Checkout POST body. Address fields are persisted as an order snapshot; card fields are transient,
/// request-scoped only, and are cleared from model state before any redisplay.
/// </summary>
public sealed class CheckoutInputModel
{
    [Required] public Guid Token { get; set; }

    [Required, StringLength(200)] public string AddressLine1 { get; set; } = string.Empty;
    [StringLength(200)] public string? AddressLine2 { get; set; }
    [Required, StringLength(100)] public string City { get; set; } = string.Empty;
    [Required, StringLength(20)] public string PostalCode { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Country { get; set; } = string.Empty;
    [Required, Phone, StringLength(30)] public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(30), Display(Name = "Name on card")]
    public string CardHolder { get; set; } = string.Empty;

    [Required, StringLength(23, MinimumLength = 12), RegularExpression(@"^[0-9 \-]+$", ErrorMessage = "Enter a valid card number."), Display(Name = "Card number")]
    public string CardNumber { get; set; } = string.Empty;

    [Required, Range(1, 12), Display(Name = "Expiry month")]
    public int ExpiryMonth { get; set; }

    [Required, Range(0, 99), Display(Name = "Expiry year")]
    public int ExpiryYear { get; set; }

    [Required, RegularExpression(@"^[0-9]{3}$", ErrorMessage = "Enter the three-digit security code."), Display(Name = "Security code")]
    public string SecurityCode { get; set; } = string.Empty;
}

public sealed class CheckoutViewModel
{
    public CheckoutInputModel Input { get; init; } = new();
    public CartViewModel Cart { get; init; } = new();
}

public sealed class OrderConfirmationLine
{
    public string ProductName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal PaidUnitPrice { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed class OrderConfirmationViewModel
{
    public long OrderId { get; init; }
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
    public IReadOnlyList<OrderConfirmationLine> Lines { get; init; } = [];
}
