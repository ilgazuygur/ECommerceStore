using ECommerceStore.Web.Services.Assistant;

namespace ECommerceStore.Tests.Assistant;

public sealed class CurrencyAmountParserTests
{
    [Theory]
    [InlineData("$2,000", "USD", 2000)]
    [InlineData("2.000 USD", "USD", 2000)]
    [InlineData("USD 2,000.50", "USD", 2000.50)]
    [InlineData("2.000,50 dollars", "USD", 2000.50)]
    [InlineData("2,000.50 TL", "TRY", 2000.50)]
    [InlineData("₺ 2.000,50", "TRY", 2000.50)]
    [InlineData("EUR 99,95", "EUR", 99.95)]
    [InlineData("€120.25", "EUR", 120.25)]
    public void Parses_currency_tokens_and_common_separator_formats(string input, string currency, decimal amount)
    {
        var parsed = Assert.Single(CurrencyAmountParser.Parse(input));
        Assert.Equal(currency, parsed.CurrencyCode);
        Assert.Equal(amount, parsed.Amount);
    }

    [Theory]
    [InlineData("2,000", 2000)]
    [InlineData("2.000", 2000)]
    [InlineData("2,000.50", 2000.50)]
    [InlineData("2.000,50", 2000.50)]
    [InlineData("99,5", 99.5)]
    public void Localized_number_parser_is_deterministic(string input, decimal expected)
    {
        Assert.True(CurrencyAmountParser.TryParseLocalizedNumber(input, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("under 100 TL", "TRY")]
    [InlineData("less than ₺2.000", "TRY")]
    [InlineData("about €50", "EUR")]
    [InlineData("100 dollars", "USD")]
    public void Finds_explicit_currency_without_reinterpreting_it(string input, string expected) =>
        Assert.Equal(expected, CurrencyAmountParser.FindExplicitCurrency(input));
}
