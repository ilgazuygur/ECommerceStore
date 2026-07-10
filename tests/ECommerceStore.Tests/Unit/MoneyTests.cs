using ECommerceStore.Web.Data;
using ECommerceStore.Web.Services.Common;

namespace ECommerceStore.Tests.Unit;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("0.01", 1L)]
    [InlineData("19.99", 1999L)]
    [InlineData("9999999999999999.99", 999999999999999999L)]
    public void Minor_unit_conversion_is_exact(string input, long expected)
    {
        var amount = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, Money.ToMinorUnits(amount));
        Assert.Equal(amount, Money.FromMinorUnits(expected));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.001")]
    [InlineData("10000000000000000.00")]
    public void Invalid_money_is_rejected(string input)
    {
        var amount = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToMinorUnits(amount));
    }

    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("2.004", "2.00")]
    [InlineData("2.995", "3.00")]
    public void Rounding_is_line_level_away_from_zero(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var expectedValue = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedValue, Money.Round(value));
    }
}

public sealed class OrderNumberGeneratorTests
{
    [Fact]
    public void Generates_opaque_fixed_length_numbers_with_date_prefix()
    {
        var generator = new OrderNumberGenerator(new FixedClock(new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc)));

        var order = generator.CreateOrderNumber();
        var invoice = generator.CreateInvoiceNumber();

        Assert.Matches("^ORD-20260710-[A-Fa-f0-9]{16}$", order);
        Assert.Matches("^INV-20260710-[A-Fa-f0-9]{16}$", invoice);
        Assert.NotEqual(order[13..], invoice[13..]);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
