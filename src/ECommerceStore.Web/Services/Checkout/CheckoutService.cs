using System.Security.Claims;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Orders;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Payments;
using ECommerceStore.Web.ViewModels.Cart;
using ECommerceStore.Web.ViewModels.Checkout;
using Microsoft.EntityFrameworkCore;

namespace ECommerceStore.Web.Services.Checkout;

public enum CheckoutOutcome
{
    Success,
    AlreadyPlaced,
    Declined,
    EmptyCart,
    StockConflict,
    Processing
}

public sealed record CheckoutResult(CheckoutOutcome Outcome, string? OrderNumber = null, long OrderId = 0, string? Message = null)
{
    public bool IsCompleted => Outcome is CheckoutOutcome.Success or CheckoutOutcome.AlreadyPlaced;
}

/// <summary>
/// Address + transient card payload for a single checkout POST. Card fields never leave this call.
/// </summary>
public sealed record PlaceOrderCommand(
    Guid Token,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string PostalCode,
    string Country,
    string PhoneNumber,
    FakePaymentRequest Payment);

public interface ICheckoutService
{
    Task<CartViewModel> GetCartAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<CheckoutResult> PlaceOrderAsync(ClaimsPrincipal user, PlaceOrderCommand command, CancellationToken cancellationToken = default);
    Task<OrderConfirmationViewModel?> GetConfirmationAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default);
}

public sealed class CheckoutService(
    ApplicationDbContext db,
    ICartService cartService,
    IPaymentService paymentService,
    IOrderNumberGenerator numbers,
    IClock clock,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    public Task<CartViewModel> GetCartAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        cartService.GetAsync(user, cancellationToken);

    public async Task<CheckoutResult> PlaceOrderAsync(ClaimsPrincipal user, PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user) ?? throw new InvalidOperationException("Checkout requires an authenticated user.");

        // Authoritative, server-side cart: current DB prices, active products, stock-capped quantities.
        var cart = await cartService.GetAsync(user, cancellationToken);
        if (cart.IsEmpty)
        {
            return new CheckoutResult(CheckoutOutcome.EmptyCart, Message: "Your cart is empty.");
        }

        var account = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.FirstName, candidate.LastName, candidate.Email, candidate.NormalizedEmail })
            .SingleAsync(cancellationToken);

        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Claim a unique (user, token) attempt. A duplicate token returns the prior outcome idempotently.
        var attempt = new CheckoutAttempt { UserId = userId, Token = command.Token, Status = CheckoutAttemptStatus.Processing };
        db.CheckoutAttempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await ResolveExistingAttemptAsync(userId, command.Token, cancellationToken);
        }

        // Fake payment. No network call, no card storage, no structured logging of the request.
        var payment = await paymentService.ProcessAsync(command.Payment, cancellationToken);
        if (!payment.Succeeded)
        {
            db.PaymentRecords.Add(BuildPaymentRecord(attempt.Id, orderId: null, payment, PaymentStatus.Failed));
            attempt.Status = CheckoutAttemptStatus.Failed;
            attempt.FailureCode = payment.ResultCode;
            attempt.CompletedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Checkout attempt {AttemptId} failed payment with sanitized code {ResultCode}.", attempt.Id, payment.ResultCode);
            return new CheckoutResult(CheckoutOutcome.Declined, Message: payment.ResultMessage);
        }

        // Atomic conditional stock claims in deterministic ProductId order; any shortfall rolls back everything.
        foreach (var line in cart.Lines.OrderBy(line => line.ProductId))
        {
            var affected = await db.Products
                .Where(product => product.Id == line.ProductId && product.IsActive && product.StockQuantity >= line.Quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(product => product.StockQuantity, product => product.StockQuantity - line.Quantity)
                    .SetProperty(product => product.Version, product => product.Version + 1)
                    .SetProperty(product => product.UpdatedAtUtc, now), cancellationToken);

            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CheckoutResult(CheckoutOutcome.StockConflict,
                    Message: "One or more items sold out or changed while you were checking out. Your cart is unchanged.");
            }
        }

        var (orderNumber, invoiceNumber) = await GenerateUniqueNumbersAsync(cancellationToken);
        var order = new Order
        {
            CheckoutAttemptId = attempt.Id,
            UserId = userId,
            OrderNumber = orderNumber,
            InvoiceNumber = invoiceNumber,
            CustomerFirstName = account.FirstName,
            CustomerLastName = account.LastName,
            CustomerEmail = account.Email!,
            CustomerEmailNormalized = account.NormalizedEmail!,
            ShippingAddressLine1 = command.AddressLine1,
            ShippingAddressLine2 = string.IsNullOrWhiteSpace(command.AddressLine2) ? null : command.AddressLine2,
            ShippingCity = command.City,
            ShippingPostalCode = command.PostalCode,
            ShippingCountry = command.Country,
            ShippingPhoneNumber = command.PhoneNumber,
            CurrencyCode = "USD",
            SubtotalAmount = cart.Totals.ListPriceSubtotal,
            DiscountAmount = cart.Totals.DiscountTotal,
            ShippingAmount = cart.Totals.Shipping,
            GrandTotal = cart.Totals.GrandTotal,
            OrderStatus = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Succeeded,
            PaidAtUtc = now,
            Items = cart.Lines.Select(line => new OrderItem
            {
                ProductId = line.ProductId,
                ProductName = line.Name,
                ProductSlug = line.Slug,
                CategoryName = line.CategoryName,
                Quantity = line.Quantity,
                ListUnitPrice = line.ListUnitPrice,
                PaidUnitPrice = line.PaidUnitPrice,
                DiscountAmount = line.LineDiscount,
                LineTotal = line.LineTotal
            }).ToList()
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        db.PaymentRecords.Add(BuildPaymentRecord(attempt.Id, order.Id, payment, PaymentStatus.Succeeded));
        await db.CartItems.Where(item => item.Cart.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        attempt.Status = CheckoutAttemptStatus.Succeeded;
        attempt.CompletedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Checkout attempt {AttemptId} created order {OrderNumber}.", attempt.Id, order.OrderNumber);
        return new CheckoutResult(CheckoutOutcome.Success, order.OrderNumber, order.Id);
    }

    public async Task<OrderConfirmationViewModel?> GetConfirmationAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        if (userId is null || string.IsNullOrWhiteSpace(orderNumber))
        {
            return null;
        }

        // Ownership is enforced in the query: both order number AND current user id are required.
        var order = await db.Orders.AsNoTracking()
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.PaymentRecord)
            .SingleOrDefaultAsync(candidate => candidate.OrderNumber == orderNumber && candidate.UserId == userId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new OrderConfirmationViewModel
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            InvoiceNumber = order.InvoiceNumber,
            CreatedAtUtc = order.CreatedAtUtc,
            OrderStatus = order.OrderStatus.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            CustomerName = $"{order.CustomerFirstName} {order.CustomerLastName}".Trim(),
            CustomerEmail = order.CustomerEmail,
            AddressLine1 = order.ShippingAddressLine1,
            AddressLine2 = order.ShippingAddressLine2,
            City = order.ShippingCity,
            PostalCode = order.ShippingPostalCode,
            Country = order.ShippingCountry,
            PhoneNumber = order.ShippingPhoneNumber,
            CardBrand = order.PaymentRecord?.CardBrand,
            CardLast4 = order.PaymentRecord?.CardLast4,
            Subtotal = order.SubtotalAmount,
            Discount = order.DiscountAmount,
            Shipping = order.ShippingAmount,
            GrandTotal = order.GrandTotal,
            Lines = order.Items
                .OrderBy(item => item.ProductName)
                .Select(item => new OrderConfirmationLine
                {
                    ProductName = item.ProductName,
                    CategoryName = item.CategoryName,
                    Quantity = item.Quantity,
                    PaidUnitPrice = item.PaidUnitPrice,
                    LineTotal = item.LineTotal
                }).ToArray()
        };
    }

    private async Task<CheckoutResult> ResolveExistingAttemptAsync(string userId, Guid token, CancellationToken cancellationToken)
    {
        var existing = await db.CheckoutAttempts.AsNoTracking()
            .Where(attempt => attempt.UserId == userId && attempt.Token == token)
            .Select(attempt => new { attempt.Status, attempt.FailureCode, OrderNumber = attempt.Order!.OrderNumber, OrderId = (long?)attempt.Order!.Id })
            .SingleOrDefaultAsync(cancellationToken);

        return existing?.Status switch
        {
            CheckoutAttemptStatus.Succeeded => new CheckoutResult(CheckoutOutcome.AlreadyPlaced, existing.OrderNumber, existing.OrderId ?? 0),
            CheckoutAttemptStatus.Failed => new CheckoutResult(CheckoutOutcome.Declined, Message: "That payment was already declined. Please try again with a different card."),
            _ => new CheckoutResult(CheckoutOutcome.Processing, Message: "Your previous order is still processing. Please wait a moment and check My Orders.")
        };
    }

    private static PaymentRecord BuildPaymentRecord(long attemptId, long? orderId, FakePaymentResult payment, PaymentStatus status) => new()
    {
        CheckoutAttemptId = attemptId,
        OrderId = orderId,
        Status = status,
        Provider = FakePaymentService.Provider,
        ProviderReference = payment.ProviderReference,
        ResultCode = payment.ResultCode,
        ResultMessage = payment.ResultMessage,
        CardBrand = payment.CardBrand,
        CardLast4 = payment.CardLast4,
        ProcessedAtUtc = payment.ProcessedAtUtc
    };

    private async Task<(string OrderNumber, string InvoiceNumber)> GenerateUniqueNumbersAsync(CancellationToken cancellationToken)
    {
        // Opaque GUID-backed numbers make collisions astronomically unlikely; the unique indexes remain the
        // final guard. A bounded pre-check regenerates on the vanishingly rare hit inside the transaction scope.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var orderNumber = numbers.CreateOrderNumber();
            var invoiceNumber = numbers.CreateInvoiceNumber();
            var clash = await db.Orders
                .AnyAsync(order => order.OrderNumber == orderNumber || order.InvoiceNumber == invoiceNumber, cancellationToken);
            if (!clash)
            {
                return (orderNumber, invoiceNumber);
            }
        }

        throw new InvalidOperationException("Unable to generate unique order/invoice numbers after several attempts.");
    }

    private static string? GetUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;
}
