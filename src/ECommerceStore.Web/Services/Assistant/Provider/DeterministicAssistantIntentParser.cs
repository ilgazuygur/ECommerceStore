using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ECommerceStore.Web.Services.Common;

namespace ECommerceStore.Web.Services.Assistant.Provider;

internal enum DeterministicAssistantIntentKind
{
    Insufficient,
    Search,
    Recommendation,
    Best,
    Cheapest,
    Availability,
    Compare,
    Ordinal,
    Alternative
}

internal enum ReferenceSelection
{
    All,
    First,
    Second,
    Third,
    Last,
    FirstTwo,
    LastTwo
}

internal sealed record DeterministicAssistantIntent(
    DeterministicAssistantIntentKind Kind,
    string NormalizedPrompt,
    string LowerPrompt,
    string? Category,
    decimal? MinimumPrice,
    decimal? MaximumPrice,
    bool AsksForStock,
    bool UsesRecentContext,
    ReferenceSelection Selection,
    string? Keyword);

internal static partial class DeterministicAssistantIntentParser
{
    private static readonly (string Alias, string Slug)[] CategoryAliases =
    [
        ("home living", "home-living"), ("home-living", "home-living"), ("ev yaşam", "home-living"),
        ("electronics", "electronics"), ("electronic", "electronics"), ("elektronik", "electronics"),
        ("accessories", "accessories"), ("accessory", "accessories"), ("aksesuar", "accessories"),
        ("fitness", "fitness"), ("sports", "fitness"), ("sport", "fitness"), ("spor", "fitness"),
        ("books", "books"), ("book", "books"), ("kitap", "books"),
        ("clothing", "clothing"), ("clothes", "clothing"), ("giyim", "clothing"),
        ("beauty", "beauty"), ("güzellik", "beauty"), ("guzellik", "beauty"),
        ("home", "home-living"), ("ev", "home-living")
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "find", "give", "tell", "me", "a", "an", "the", "some", "something", "useful", "products", "product",
        "items", "item", "please", "what", "which", "how", "one", "ones", "should", "i", "you", "do", "does", "did", "is", "are",
        "it", "they", "have", "has", "still", "yet", "for", "about",
        "recommend", "recommended", "suggest", "buy", "order", "choose", "want", "need", "try", "of", "to", "and", "or",
        "under", "below", "less", "than", "between", "from", "in", "stock", "available", "availability", "usd", "dollars",
        "dollar", "tl", "try", "eur", "euro", "euros", "first", "second", "third", "last", "two", "those", "them",
        "compare", "cheaper", "cheapest", "best", "alternative", "similar", "but", "current", "currently",
        "göster", "goster", "bul", "bana", "bir", "şey", "sey", "ürün", "urun", "ürünü", "urunu", "ürünler", "urunler", "hangi", "hangisi",
        "hangisini", "ne", "öner", "önerirsin", "oner", "onerirsin", "almalıyım", "almaliyim", "almalıyim", "sipariş",
        "siparis", "vermeliyim", "için", "icin", "altında", "altinda", "arasında", "arasinda", "stok", "stokta", "mevcut",
        "ilk", "birinci", "birincisi", "ikincisi", "ikinci", "üçüncü", "ucuncu", "üçüncüsü", "ucuncusu",
        "son", "sonuncu", "sonuncusu", "ikisini", "bunlar", "bunları", "bunlari",
        "karşılaştır", "karsilastir", "daha", "ucuz", "iyi", "en", "alternatif", "benzer", "nasıl", "nasil",
        "mi", "mı", "mu", "mü", "hala", "hâlâ", "bunlardan"
    };

    public static DeterministicAssistantIntent Parse(string prompt)
    {
        var normalized = AssistantPromptText.Normalize(prompt);
        var lower = FoldForParsing(normalized);
        if (!AssistantPromptText.IsMeaningful(normalized))
            return Create(DeterministicAssistantIntentKind.Insufficient, normalized, lower);

        var category = FindCategory(lower);
        var amounts = CurrencyAmountParser.Parse(normalized);
        var asksForRange = amounts.Count > 0 && ContainsAny(lower,
            "under", "below", "less than", "between", "budget", "altında", "altinda", "arasında", "arasinda", "bütçe", "butce");
        decimal? minimum = null;
        decimal? maximum = null;
        if (asksForRange)
        {
            minimum = ContainsAny(lower, "between", "arasında", "arasinda") && amounts.Count > 1
                ? amounts.Min(amount => amount.Amount)
                : 0m;
            maximum = amounts.Max(amount => amount.Amount);
        }

        var asksForStock = ContainsAny(lower, "in stock", "stock", "available", "availability", "stokta", "stok", "mevcut");
        var selection = FindSelection(lower);
        var hasReferenceLanguage = HasReferenceLanguage(lower, selection);

        if (ContainsAny(lower, "alternative", "similar", "alternatif", "benzer"))
            return Create(DeterministicAssistantIntentKind.Alternative, normalized, lower, category, minimum, maximum,
                asksForStock, true, selection, MeaningfulKeyword(lower, category));

        if (ContainsAny(lower, "cheapest", "cheaper", "lowest price", "daha ucuz", "en ucuz"))
            return Create(DeterministicAssistantIntentKind.Cheapest, normalized, lower, category, minimum, maximum,
                asksForStock, hasReferenceLanguage, selection, MeaningfulKeyword(lower, category));

        if (ContainsAny(lower, "which one is best", "which is best", "which one is better", "which is better",
                "which one should i choose", "which should i choose", "which one should i buy", "which should i buy",
                "which one should i order", "which should i order", "which one do you recommend",
                "hangisi daha iyi", "hangisi en iyi", "hangisini almalıyım", "hangisini almaliyim", "hangisini almalıyim",
                "hangisini siparis vermeliyim"))
            return Create(DeterministicAssistantIntentKind.Best, normalized, lower, category, minimum, maximum,
                asksForStock, true, selection, MeaningfulKeyword(lower, category));

        if (ContainsAny(lower, "compare", "comparison", "karşılaştır", "karsilastir"))
            return Create(DeterministicAssistantIntentKind.Compare, normalized, lower, category, minimum, maximum,
                asksForStock, true, selection, MeaningfulKeyword(lower, category));

        if (asksForStock)
            return Create(DeterministicAssistantIntentKind.Availability, normalized, lower, category, minimum, maximum,
                true, hasReferenceLanguage, selection, MeaningfulKeyword(lower, category));

        if (selection != ReferenceSelection.All)
            return Create(DeterministicAssistantIntentKind.Ordinal, normalized, lower, category, minimum, maximum,
                false, true, selection, MeaningfulKeyword(lower, category));

        var recommendation = ContainsAny(lower,
            "what should i buy", "what do you recommend", "recommend", "suggest", "something useful", "what should i order",
            "bana ne önerirsin", "bana ne onerirsin", "ne almalıyım", "ne almaliyim", "ne almalıyim", "bir şey öner", "bir sey oner",
            "hangi ürünü almalıyım", "hangi urunu almaliyim", "hangi ürünü almaliyim", "hangi urunu almalıyım");
        if (recommendation)
            return Create(DeterministicAssistantIntentKind.Recommendation, normalized, lower, category, minimum, maximum,
                false, false, selection, MeaningfulKeyword(lower, category));

        var keyword = MeaningfulKeyword(lower, category);
        return keyword is null && category is null && maximum is null
            ? Create(DeterministicAssistantIntentKind.Insufficient, normalized, lower)
            : Create(DeterministicAssistantIntentKind.Search, normalized, lower, category, minimum, maximum,
                false, false, selection, keyword);
    }

    private static DeterministicAssistantIntent Create(
        DeterministicAssistantIntentKind kind,
        string normalized,
        string lower,
        string? category = null,
        decimal? minimum = null,
        decimal? maximum = null,
        bool asksForStock = false,
        bool usesRecentContext = false,
        ReferenceSelection selection = ReferenceSelection.All,
        string? keyword = null) =>
        new(kind, normalized, lower, category, minimum, maximum, asksForStock, usesRecentContext, selection, keyword);

    private static string? FindCategory(string prompt)
    {
        foreach (var (alias, slug) in CategoryAliases)
            if (Phrase(prompt, alias)) return slug;
        return null;
    }

    private static ReferenceSelection FindSelection(string prompt)
    {
        if (ContainsPhraseAny(prompt, "first two", "ilk ikisini", "ilk iki")) return ReferenceSelection.FirstTwo;
        if (ContainsPhraseAny(prompt, "last two", "son ikisini", "son iki")) return ReferenceSelection.LastTwo;
        if (ContainsPhraseAny(prompt, "second", "ikincisi", "ikinci")) return ReferenceSelection.Second;
        if (ContainsPhraseAny(prompt, "third", "üçüncü", "ucuncu", "üçüncüsü", "ucuncusu")) return ReferenceSelection.Third;
        if (ContainsPhraseAny(prompt, "first", "birinci", "birincisi", "ilk")) return ReferenceSelection.First;
        if (ContainsPhraseAny(prompt, "last one", "the last", "sonuncu", "sonuncusu")) return ReferenceSelection.Last;
        return ReferenceSelection.All;
    }

    private static bool HasReferenceLanguage(string prompt, ReferenceSelection selection) =>
        selection != ReferenceSelection.All || new[]
        {
            "which", "those", "them", "of these", "of those", "that one", "this one",
            "hangisi", "hangisini", "bunlar", "bunları", "bunlari"
        }.Any(value => Phrase(prompt, value));

    private static string? MeaningfulKeyword(string prompt, string? category)
    {
        var categoryAliases = CategoryAliases.Where(item => item.Slug == category).SelectMany(item => item.Alias.Split(' ')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var words = WordPattern().Matches(prompt).Select(match => match.Value)
            .Where(word => !StopWords.Contains(word) && !categoryAliases.Contains(word))
            .Where(word => !decimal.TryParse(word, out _))
            .Where(AssistantPromptText.IsMeaningful)
            .Take(6).ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static bool Phrase(string prompt, string phrase) =>
        Regex.IsMatch(prompt, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant);

    private static bool ContainsAny(string prompt, params string[] values) =>
        values.Any(value => prompt.Contains(value, StringComparison.Ordinal));

    private static bool ContainsPhraseAny(string prompt, params string[] values) =>
        values.Any(value => Phrase(prompt, value));

    private static string FoldForParsing(string value)
    {
        string decomposed;
        try
        {
            decomposed = value.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            return value.ToLowerInvariant();
        }
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character == 'ı' ? 'i' : character);
        return builder.ToString().ToLowerInvariant().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
