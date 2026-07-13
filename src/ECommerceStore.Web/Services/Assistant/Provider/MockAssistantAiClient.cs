using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ECommerceStore.Web.Services.Assistant.Provider;

public sealed partial class MockAssistantAiClient : IAssistantAiClient
{
    public Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Messages.LastOrDefault()?.Role == "tool")
            return Task.FromResult(new AssistantAiResponse(BuildToolReply(request.Messages), []));

        var prompt = request.Messages.LastOrDefault(message => message.Role == "user")?.Content?.Trim() ?? string.Empty;
        var lower = prompt.ToLowerInvariant();
        var references = ReadReferenceIds(request.Messages);
        var calls = new List<AssistantToolCall>();
        var hasCategory = TryCategory(lower, out var category);
        var asksForStock = lower.Contains("stock", StringComparison.Ordinal) || lower.Contains("available", StringComparison.Ordinal);
        var amounts = CurrencyAmountParser.Parse(prompt);
        var asksForRange = amounts.Count > 0 && (lower.Contains("under", StringComparison.Ordinal) ||
            lower.Contains("below", StringComparison.Ordinal) || lower.Contains("between", StringComparison.Ordinal));

        if (references.Count > 0 && ContainsReference(lower))
        {
            var selected = SelectReferences(lower, references);
            calls.AddRange(selected.Select((id, index) => Call(index, "GetProductDetails", new { productId = id })));
        }
        else if (hasCategory && (asksForStock || asksForRange))
        {
            var minimum = lower.Contains("between", StringComparison.Ordinal) && amounts.Count > 1 ? amounts.Min(item => item.Amount) : (decimal?)null;
            var maximum = asksForRange ? amounts.Max(item => item.Amount) : (decimal?)null;
            calls.Add(Call(0, "SearchProducts", new
            {
                keyword = (string?)null,
                category,
                minimumPrice = minimum,
                maximumPrice = maximum,
                inStockOnly = asksForStock,
                limit = 6
            }));
        }
        else if (hasCategory)
        {
            calls.Add(Call(0, "GetProductsByCategory", new { category, limit = 6 }));
        }
        else if (asksForStock)
        {
            calls.Add(Call(0, "GetInStockProducts", new { keyword = MeaningfulKeyword(prompt), limit = 6 }));
        }
        else
        {
            if (asksForRange)
            {
                var minimum = lower.Contains("between", StringComparison.Ordinal) && amounts.Count > 1 ? amounts.Min(item => item.Amount) : 0m;
                var maximum = amounts.Max(item => item.Amount);
                calls.Add(Call(0, "GetProductsWithinPriceRange", new { minimumPrice = minimum, maximumPrice = maximum, keyword = MeaningfulKeyword(prompt), limit = 6 }));
            }
            else
            {
                calls.Add(Call(0, "SearchProducts", new { keyword = MeaningfulKeyword(prompt), limit = 6 }));
            }
        }

        return Task.FromResult(new AssistantAiResponse(null, calls, "tool_calls"));
    }

    private static AssistantToolCall Call(int index, string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return new AssistantToolCall($"mock-call-{index + 1}", name, document.RootElement.Clone());
    }

    private static string BuildToolReply(IReadOnlyList<AssistantAiMessage> messages)
    {
        var prompt = messages.LastOrDefault(message => message.Role == "user")?.Content?.ToLowerInvariant() ?? string.Empty;
        var products = new List<ToolProduct>();
        foreach (var message in messages.Where(message => message.Role == "tool" && !string.IsNullOrWhiteSpace(message.Content)))
        {
            try
            {
                using var document = JsonDocument.Parse(message.Content!);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var id = item.GetProperty("Id").GetInt32();
                    if (products.Any(product => product.Id == id)) continue;
                    products.Add(new ToolProduct(
                        id,
                        item.GetProperty("Name").GetString() ?? "Product",
                        item.GetProperty("EffectivePrice").GetDecimal(),
                        item.GetProperty("StockQuantity").GetInt32()));
                }
            }
            catch (JsonException)
            {
                // The orchestration layer generated these tool messages. If one is
                // malformed, keep the mock response deterministic and disclose no data.
            }
        }

        if (products.Count == 0) return "I couldn’t find a current public catalogue match for that request.";
        if (prompt.Contains("cheaper", StringComparison.Ordinal) || prompt.Contains("cheapest", StringComparison.Ordinal))
        {
            var cheapest = products.OrderBy(product => product.Price).ThenBy(product => product.Id).First();
            return $"{cheapest.Name} is currently the cheapest of those at {cheapest.Price.ToString("0.00", CultureInfo.InvariantCulture)}.";
        }
        if (products.Count == 1 && (prompt.Contains("stock", StringComparison.Ordinal) || prompt.Contains("available", StringComparison.Ordinal)))
        {
            var product = products[0];
            return product.StockQuantity > 0
                ? $"{product.Name} is currently in stock ({product.StockQuantity} available)."
                : $"{product.Name} is currently out of stock.";
        }
        if (prompt.Contains("compare", StringComparison.Ordinal))
        {
            var ordered = products.OrderBy(product => product.Price).ThenBy(product => product.Id).Take(4);
            return "Current price comparison: " + string.Join(", ", ordered.Select(product =>
                $"{product.Name} at {product.Price.ToString("0.00", CultureInfo.InvariantCulture)}")) + ".";
        }
        return $"I found {products.Count} current catalogue match{(products.Count == 1 ? string.Empty : "es")}: {string.Join(", ", products.Select(product => product.Name))}.";
    }

    private static List<int> ReadReferenceIds(IReadOnlyList<AssistantAiMessage> messages)
    {
        var line = messages.LastOrDefault(message => message.Role == "system" && message.Content?.StartsWith("REFERENCE_PRODUCT_IDS:", StringComparison.Ordinal) == true)?.Content;
        if (line is null) return [];
        return line["REFERENCE_PRODUCT_IDS:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0).Where(id => id > 0).ToList();
    }

    private static bool ContainsReference(string prompt) =>
        prompt.Contains("first", StringComparison.Ordinal) || prompt.Contains("second", StringComparison.Ordinal) ||
        prompt.Contains("third", StringComparison.Ordinal) || prompt.Contains("last", StringComparison.Ordinal) ||
        prompt.Contains("those", StringComparison.Ordinal) || prompt.Contains("them", StringComparison.Ordinal) ||
        prompt.Contains("cheaper", StringComparison.Ordinal) || prompt.Contains("compare", StringComparison.Ordinal);

    private static IReadOnlyList<int> SelectReferences(string prompt, IReadOnlyList<int> references)
    {
        if (prompt.Contains("last two", StringComparison.Ordinal)) return references.TakeLast(2).ToArray();
        if (prompt.Contains("second", StringComparison.Ordinal) && references.Count > 1) return [references[1]];
        if (prompt.Contains("third", StringComparison.Ordinal) && references.Count > 2) return [references[2]];
        if (prompt.Contains("first", StringComparison.Ordinal)) return [references[0]];
        if (prompt.Contains("last", StringComparison.Ordinal)) return [references[^1]];
        return references.TakeLast(Math.Min(4, references.Count)).ToArray();
    }

    private static bool TryCategory(string prompt, out string category)
    {
        foreach (var candidate in new[] { "electronics", "home", "books", "clothing", "beauty", "sports" })
        {
            if (prompt.Contains(candidate, StringComparison.Ordinal))
            {
                category = candidate;
                return true;
            }
        }
        category = string.Empty;
        return false;
    }

    private static string? MeaningfulKeyword(string prompt)
    {
        var words = WordPattern().Matches(prompt.ToLowerInvariant()).Select(match => match.Value)
            .Where(word => !StopWords.Contains(word)).Take(6).ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "find", "me", "a", "an", "the", "some", "products", "product", "items", "item", "please",
        "under", "below", "between", "in", "stock", "available", "usd", "dollars", "dollar", "try", "tl", "eur", "euro", "euros",
        "first", "second", "third", "last", "two", "those", "them", "compare", "cheaper", "alternative", "similar", "but"
    };

    [GeneratedRegex(@"[\p{L}\p{N}_%-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    private sealed record ToolProduct(int Id, string Name, decimal Price, int StockQuantity);
}
