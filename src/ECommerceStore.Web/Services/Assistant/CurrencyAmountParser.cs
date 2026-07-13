using System.Globalization;
using System.Text.RegularExpressions;

namespace ECommerceStore.Web.Services.Assistant;

public sealed record ParsedCurrencyAmount(string? CurrencyCode, decimal Amount, string Source);

public static partial class CurrencyAmountParser
{
    private static readonly Dictionary<string, string> CurrencyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$"] = "USD", ["usd"] = "USD", ["dollar"] = "USD", ["dollars"] = "USD",
        ["tl"] = "TRY", ["try"] = "TRY", ["₺"] = "TRY", ["lira"] = "TRY",
        ["eur"] = "EUR", ["€"] = "EUR", ["euro"] = "EUR", ["euros"] = "EUR"
    };

    public static IReadOnlyList<ParsedCurrencyAmount> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var results = new List<ParsedCurrencyAmount>();
        foreach (Match match in AmountPattern().Matches(text))
        {
            var prefix = match.Groups["prefix"].Value;
            var suffix = match.Groups["suffix"].Value;
            var token = string.IsNullOrWhiteSpace(prefix) ? suffix : prefix;
            if (!TryParseLocalizedNumber(match.Groups["amount"].Value, out var amount)) continue;
            results.Add(new ParsedCurrencyAmount(
                CurrencyTokens.TryGetValue(token.Trim(), out var currency) ? currency : null,
                amount,
                match.Value));
        }
        return results;
    }

    public static string? FindExplicitCurrency(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (Match match in CurrencyPattern().Matches(text))
            if (CurrencyTokens.TryGetValue(match.Value, out var code)) return code;
        return null;
    }

    public static bool TryParseLocalizedNumber(string raw, out decimal value)
    {
        value = 0;
        var text = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!NumberShape().IsMatch(text)) return false;
        var comma = text.LastIndexOf(',');
        var dot = text.LastIndexOf('.');
        char? decimalSeparator = null;

        if (comma >= 0 && dot >= 0)
        {
            var last = Math.Max(comma, dot);
            var trailing = text.Length - last - 1;
            if (trailing is 1 or 2) decimalSeparator = text[last];
        }
        else if (comma >= 0 || dot >= 0)
        {
            var separator = comma >= 0 ? ',' : '.';
            var occurrences = text.Count(character => character == separator);
            var trailing = text.Length - text.LastIndexOf(separator) - 1;
            if (trailing is 1 or 2) decimalSeparator = separator;
            else if (occurrences > 1 && trailing != 3) return false;
        }

        var normalized = decimalSeparator.HasValue
            ? RemoveGroupingAndNormalizeDecimal(text, decimalSeparator.Value)
            : text.Replace(",", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value is >= 0 and <= 1_000_000_000m;
    }

    private static string RemoveGroupingAndNormalizeDecimal(string text, char decimalSeparator)
    {
        var position = text.LastIndexOf(decimalSeparator);
        var whole = text[..position].Replace(",", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal);
        return whole + "." + text[(position + 1)..];
    }

    [GeneratedRegex(@"(?<![\p{L}\d])(?:(?<prefix>USD|TRY|TL|EUR|[$₺€])\s*)?(?<amount>\d{1,3}(?:[.,\s]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)(?:\s*(?<suffix>USD|TRY|TL|EUR|dollars?|lira|euros?|[$₺€]))?(?![\p{L}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"(?<![\p{L}])(USD|TRY|TL|EUR|dollars?|lira|euros?|[$₺€])(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex(@"^\d+(?:[.,]\d+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberShape();
}
