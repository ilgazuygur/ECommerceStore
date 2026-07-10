using ECommerceStore.Web.Services.Common;

namespace ECommerceStore.Web.Services.Payments;

/// <summary>
/// Transient, request-scoped card input. This object exists only in controller/service memory and is
/// never persisted, structured-logged, or serialized. The full number and CVV never leave this type.
/// </summary>
public sealed record FakePaymentRequest(string CardNumber, string CardHolder, int ExpiryMonth, int ExpiryYear, string SecurityCode);

/// <summary>
/// Sanitized fake payment outcome. Contains only allow-listed, non-sensitive metadata that is safe to
/// persist and display: never the full PAN or CVV.
/// </summary>
public sealed record FakePaymentResult(
    bool Succeeded,
    string ResultCode,
    string ResultMessage,
    string ProviderReference,
    string? CardBrand,
    string? CardLast4,
    DateTime ProcessedAtUtc);

public interface IPaymentService
{
    Task<FakePaymentResult> ProcessAsync(FakePaymentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic, provider-independent fake payment gateway. No network call is ever made.
/// <c>4242424242424242</c> approves; <c>4000000000000002</c> deterministically declines; every other
/// structurally valid card is unsupported. Malformed or expired inputs fail validation.
/// </summary>
public sealed class FakePaymentService(IClock clock) : IPaymentService
{
    public const string SuccessCard = "4242424242424242";
    public const string DeclineCard = "4000000000000002";
    public const string Provider = "Fake";

    public Task<FakePaymentResult> ProcessAsync(FakePaymentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        // Normalize spaces/hyphens in memory only.
        var number = new string((request.CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        var code = (request.SecurityCode ?? string.Empty).Trim();
        var now = clock.UtcNow;

        if (number.Length is < 13 or > 19 || !PassesLuhn(number))
        {
            return Result(false, "InvalidCard", "Enter a valid card number.", null, null, now);
        }

        if (code.Length != 3 || !code.All(char.IsDigit))
        {
            return Result(false, "InvalidCard", "Enter the three-digit security code.", DetectBrand(number), Last4(number), now);
        }

        if (request.ExpiryMonth is < 1 or > 12 || !IsFutureExpiry(request.ExpiryMonth, request.ExpiryYear, now))
        {
            return Result(false, "Expired", "The card expiry date is invalid or in the past.", DetectBrand(number), Last4(number), now);
        }

        return number switch
        {
            SuccessCard => Result(true, "Approved", "Payment approved.", DetectBrand(number), Last4(number), now),
            DeclineCard => Result(false, "Declined", "The card was declined. Please try a different card.", DetectBrand(number), Last4(number), now),
            _ => Result(false, "Unsupported", "This card is not supported by the demo gateway.", DetectBrand(number), Last4(number), now)
        };
    }

    private static Task<FakePaymentResult> Result(bool succeeded, string code, string message, string? brand, string? last4, DateTime now) =>
        Task.FromResult(new FakePaymentResult(succeeded, code, message, $"{Provider.ToUpperInvariant()}-{Guid.NewGuid():N}", brand, last4, now));

    private static bool IsFutureExpiry(int month, int year, DateTime now)
    {
        var normalizedYear = year switch
        {
            >= 0 and <= 99 => 2000 + year,
            _ => year
        };
        if (normalizedYear is < 2000 or > 2100)
        {
            return false;
        }

        // The card remains valid through the last moment of its expiry month.
        var lastDay = DateTime.DaysInMonth(normalizedYear, month);
        var expiry = new DateTime(normalizedYear, month, lastDay, 23, 59, 59, DateTimeKind.Utc);
        return expiry >= now;
    }

    private static string? Last4(string number) => number.Length >= 4 ? number[^4..] : null;

    private static string? DetectBrand(string number) => number.Length switch
    {
        >= 1 when number[0] == '4' => "Visa",
        >= 2 when number[..2] is "51" or "52" or "53" or "54" or "55" => "Mastercard",
        >= 2 when number[..2] is "34" or "37" => "American Express",
        _ => "Card"
    };

    private static bool PassesLuhn(string number)
    {
        var sum = 0;
        var alternate = false;
        for (var i = number.Length - 1; i >= 0; i--)
        {
            var digit = number[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}
