using System.Text;
using System.Text.RegularExpressions;

namespace ECommerceStore.Web.Services.Assistant;

internal static partial class AssistantPromptText
{
    private static readonly HashSet<string> SupportedShortTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "tv", "pc", "4k", "ai"
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string unicode;
        try
        {
            unicode = value.EnumerateRunes().Any(rune => Rune.IsLetterOrDigit(rune))
                ? value.Normalize(NormalizationForm.FormKC)
                : value;
        }
        catch (ArgumentException)
        {
            unicode = value;
        }
        return WhitespacePattern().Replace(unicode, " ").Trim();
    }

    public static bool IsMeaningful(string value)
    {
        var tokens = TokenPattern().Matches(value).Select(match => match.Value).ToArray();
        if (tokens.Length == 0) return false;

        return tokens.Any(token =>
        {
            if (SupportedShortTerms.Contains(token)) return true;
            if (token.EnumerateRunes().Count() < 2) return false;
            var normalized = token.ToLowerInvariant();
            return normalized.EnumerateRunes().Select(rune => rune.Value).Distinct().Take(2).Count() > 1;
        });
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
