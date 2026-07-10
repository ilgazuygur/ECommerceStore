using ECommerceStore.Web.Models.Orders;

namespace ECommerceStore.Web.Services.Admin;

/// <summary>
/// The admin order-status state machine from ARCHITECTURE.md §10.4. Fulfilment statuses that imply money
/// changed hands (Paid, Processing, Shipped, Delivered) require a successful payment. Changing order
/// status never alters payment status.
/// </summary>
public static class OrderStatusPolicy
{
    private static readonly IReadOnlyDictionary<OrderStatus, IReadOnlyList<OrderStatus>> Transitions =
        new Dictionary<OrderStatus, IReadOnlyList<OrderStatus>>
        {
            [OrderStatus.Pending] = [OrderStatus.Paid, OrderStatus.Cancelled],
            [OrderStatus.Paid] = [OrderStatus.Processing, OrderStatus.Cancelled],
            [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };

    private static readonly IReadOnlySet<OrderStatus> RequiresSuccessfulPayment =
        new HashSet<OrderStatus> { OrderStatus.Paid, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered };

    public static IReadOnlyList<OrderStatus> AllowedTransitions(OrderStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : [];

    public static bool CanTransition(OrderStatus from, OrderStatus to, PaymentStatus paymentStatus)
    {
        if (from == to || !AllowedTransitions(from).Contains(to))
        {
            return false;
        }

        return !RequiresSuccessfulPayment.Contains(to) || paymentStatus == PaymentStatus.Succeeded;
    }
}
