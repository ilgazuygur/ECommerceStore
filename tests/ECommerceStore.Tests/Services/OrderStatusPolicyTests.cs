using ECommerceStore.Web.Models.Orders;
using ECommerceStore.Web.Services.Admin;

namespace ECommerceStore.Tests.Services;

public sealed class OrderStatusPolicyTests
{
    [Theory]
    [InlineData(OrderStatus.Paid, OrderStatus.Processing, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Processing, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Paid, true)]
    public void Valid_transitions_are_allowed_when_paid(OrderStatus from, OrderStatus to, bool expected) =>
        Assert.Equal(expected, OrderStatusPolicy.CanTransition(from, to, PaymentStatus.Succeeded));

    [Theory]
    [InlineData(OrderStatus.Paid, OrderStatus.Delivered)]      // skips states
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]   // backwards
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid)]      // terminal
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]   // not allowed after shipping
    [InlineData(OrderStatus.Paid, OrderStatus.Paid)]           // no-op
    public void Invalid_transitions_are_rejected(OrderStatus from, OrderStatus to) =>
        Assert.False(OrderStatusPolicy.CanTransition(from, to, PaymentStatus.Succeeded));

    [Fact]
    public void Fulfilment_transitions_require_successful_payment()
    {
        // Pending -> Paid demands a successful payment.
        Assert.False(OrderStatusPolicy.CanTransition(OrderStatus.Pending, OrderStatus.Paid, PaymentStatus.Failed));
        Assert.True(OrderStatusPolicy.CanTransition(OrderStatus.Pending, OrderStatus.Paid, PaymentStatus.Succeeded));
        // Cancelling never requires payment.
        Assert.True(OrderStatusPolicy.CanTransition(OrderStatus.Pending, OrderStatus.Cancelled, PaymentStatus.Failed));
    }

    [Fact]
    public void Terminal_states_have_no_transitions()
    {
        Assert.Empty(OrderStatusPolicy.AllowedTransitions(OrderStatus.Delivered));
        Assert.Empty(OrderStatusPolicy.AllowedTransitions(OrderStatus.Cancelled));
    }
}
