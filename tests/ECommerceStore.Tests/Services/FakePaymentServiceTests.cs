using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Payments;

namespace ECommerceStore.Tests.Services;

public sealed class FakePaymentServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    private static FakePaymentService Service() => new(new StubClock(Now));

    [Fact]
    public async Task Success_card_with_future_expiry_and_three_digit_code_is_approved()
    {
        var result = await Service().ProcessAsync(new FakePaymentRequest("4242 4242 4242 4242", "Jane Buyer", 12, 30, "123"));

        Assert.True(result.Succeeded);
        Assert.Equal("Approved", result.ResultCode);
        Assert.Equal("Visa", result.CardBrand);
        Assert.Equal("4242", result.CardLast4);
        Assert.StartsWith("FAKE-", result.ProviderReference);
    }

    [Fact]
    public async Task Decline_card_deterministically_fails_without_approval()
    {
        var result = await Service().ProcessAsync(new FakePaymentRequest("4000000000000002", "Jane Buyer", 6, 30, "999"));

        Assert.False(result.Succeeded);
        Assert.Equal("Declined", result.ResultCode);
        Assert.Equal("0002", result.CardLast4);
    }

    [Fact]
    public async Task Other_structurally_valid_card_is_unsupported()
    {
        // 4111111111111111 is Luhn-valid but is neither the success nor decline card.
        var result = await Service().ProcessAsync(new FakePaymentRequest("4111111111111111", "Jane Buyer", 6, 30, "123"));

        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.ResultCode);
    }

    [Fact]
    public async Task Non_luhn_number_is_rejected_as_invalid()
    {
        var result = await Service().ProcessAsync(new FakePaymentRequest("4242424242424241", "Jane Buyer", 6, 30, "123"));

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidCard", result.ResultCode);
        Assert.Null(result.CardLast4);
    }

    [Fact]
    public async Task Expired_card_fails()
    {
        var result = await Service().ProcessAsync(new FakePaymentRequest("4242424242424242", "Jane Buyer", 6, 25, "123"));

        Assert.False(result.Succeeded);
        Assert.Equal("Expired", result.ResultCode);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("1234")]
    [InlineData("abc")]
    public async Task Bad_security_code_fails(string code)
    {
        var result = await Service().ProcessAsync(new FakePaymentRequest("4242424242424242", "Jane Buyer", 12, 30, code));

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidCard", result.ResultCode);
    }

    [Fact]
    public async Task Sanitized_result_never_contains_full_pan_or_cvv()
    {
        var result = await Service().ProcessAsync(new FakePaymentRequest("4242424242424242", "Jane Buyer", 12, 30, "321"));

        var payload = string.Join("|", result.ResultCode, result.ResultMessage, result.ProviderReference, result.CardBrand, result.CardLast4);
        Assert.DoesNotContain("4242424242424242", payload);
        Assert.DoesNotContain("321", payload);
    }

    private sealed class StubClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
