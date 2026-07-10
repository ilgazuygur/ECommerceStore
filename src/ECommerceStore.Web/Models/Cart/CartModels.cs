using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;

namespace ECommerceStore.Web.Models.Cart;

public sealed class ShoppingCart
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; } = 1;
    public ApplicationUser User { get; set; } = null!;
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public sealed class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ShoppingCart Cart { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
