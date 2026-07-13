using System.Globalization;
using System.Text.Json;

namespace ECommerceStore.Web.Services.Assistant.Provider;

public sealed class MockAssistantAiClient : IAssistantAiClient
{
    private const int RecommendationLimit = 3;
    private const int CatalogueLimit = 5;
    private const string Guidance = "Tell me a product, category, budget, or what you need it for, and I’ll search the current catalogue.";

    public Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = request.Messages.LastOrDefault(message => message.Role == "user")?.Content ?? string.Empty;
        var intent = DeterministicAssistantIntentParser.Parse(prompt);
        if (request.Messages.LastOrDefault()?.Role == "tool")
            return Task.FromResult(new AssistantAiResponse(BuildToolReply(request.Messages, intent), []));

        if (intent.Kind == DeterministicAssistantIntentKind.Insufficient)
            return Task.FromResult(new AssistantAiResponse(Guidance, []));

        var references = ReadReferences(request.Messages);
        if (ShouldUseReferences(intent, references))
        {
            if (intent.Kind == DeterministicAssistantIntentKind.Alternative && intent.Selection == ReferenceSelection.All)
            {
                return Task.FromResult(new AssistantAiResponse(
                    "Tell me which recent product you want an alternative to, such as the first or second one.", []));
            }

            var selected = SelectReferences(intent.Selection, references);
            var ids = selected.Where(reference => reference.ProductId is > 0)
                .Select(reference => reference.ProductId!.Value).Distinct().ToArray();
            if (ids.Length == 0)
                return Task.FromResult(new AssistantAiResponse(
                    "That referenced product is no longer available in the current public catalogue. Ask me to show a fresh category or name another product.", []));

            if (intent.Kind == DeterministicAssistantIntentKind.Alternative)
            {
                var candidateIds = references.Where(reference => reference.ProductId is > 0 && reference.ProductId != ids[0])
                    .Select(reference => reference.ProductId!.Value).Distinct().Take(5).ToArray();
                return Task.FromResult(ToolCalls(
                [
                    Call(0, "GetProductAlternatives", new
                    {
                        productId = ids[0],
                        candidateProductIds = candidateIds,
                        cheaperOnly = IsCheaper(intent.LowerPrompt),
                        limit = RecommendationLimit
                    })
                ]));
            }

            return Task.FromResult(ToolCalls(ids.Select((id, index) =>
                Call(index, "GetProductDetails", new { productId = id }))));
        }

        if (RequiresRecentContext(intent))
            return Task.FromResult(new AssistantAiResponse(
                "I don’t have a recent product set to compare. Ask me to show a category or name a product first.", []));

        var calls = BuildSearchCalls(intent);
        if (calls.Count == 0)
            return Task.FromResult(new AssistantAiResponse(Guidance, []));
        return Task.FromResult(new AssistantAiResponse(null, calls, "tool_calls"));
    }

    private static List<AssistantToolCall> BuildSearchCalls(DeterministicAssistantIntent intent)
    {
        if (intent.Kind is DeterministicAssistantIntentKind.Recommendation or DeterministicAssistantIntentKind.Cheapest)
        {
            if (intent.Category is not null && intent.MinimumPrice is null && intent.MaximumPrice is null && intent.Keyword is null)
                return [Call(0, "GetProductsByCategory", new { category = intent.Category, limit = RecommendationLimit + 1 })];

            if (intent.Category is null && intent.MinimumPrice is null && intent.MaximumPrice is null && intent.Keyword is null)
                return [Call(0, "GetInStockProducts", new { keyword = (string?)null, limit = RecommendationLimit })];

            return [Call(0, "SearchProducts", new
            {
                keyword = intent.Keyword,
                category = intent.Category,
                minimumPrice = intent.MinimumPrice,
                maximumPrice = intent.MaximumPrice,
                inStockOnly = true,
                limit = RecommendationLimit
            })];
        }

        if (intent.Kind == DeterministicAssistantIntentKind.Availability)
        {
            if (intent.Category is not null || intent.MinimumPrice.HasValue || intent.MaximumPrice.HasValue)
                return [Call(0, "SearchProducts", new
                {
                    keyword = intent.Keyword,
                    category = intent.Category,
                    minimumPrice = intent.MinimumPrice,
                    maximumPrice = intent.MaximumPrice,
                    inStockOnly = true,
                    limit = CatalogueLimit
                })];
            return [Call(0, "GetInStockProducts", new { keyword = intent.Keyword, limit = CatalogueLimit })];
        }

        if (intent.Category is not null && (intent.MinimumPrice.HasValue || intent.MaximumPrice.HasValue))
            return [Call(0, "SearchProducts", new
            {
                keyword = intent.Keyword,
                category = intent.Category,
                minimumPrice = intent.MinimumPrice,
                maximumPrice = intent.MaximumPrice,
                inStockOnly = false,
                limit = CatalogueLimit
            })];
        if (intent.Category is not null)
            return [Call(0, "GetProductsByCategory", new { category = intent.Category, limit = CatalogueLimit })];
        if (intent.MaximumPrice.HasValue)
            return [Call(0, "GetProductsWithinPriceRange", new
            {
                minimumPrice = intent.MinimumPrice ?? 0m,
                maximumPrice = intent.MaximumPrice.Value,
                keyword = intent.Keyword,
                limit = CatalogueLimit
            })];
        return intent.Keyword is null
            ? []
            : [Call(0, "SearchProducts", new { keyword = intent.Keyword, limit = CatalogueLimit })];
    }

    private static AssistantAiResponse ToolCalls(IEnumerable<AssistantToolCall> calls) =>
        new(null, calls.ToArray(), "tool_calls");

    private static AssistantToolCall Call(int index, string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return new AssistantToolCall($"mock-call-{index + 1}", name, document.RootElement.Clone());
    }

    private static string BuildToolReply(IReadOnlyList<AssistantAiMessage> messages, DeterministicAssistantIntent intent)
    {
        var products = ReadToolProducts(messages);
        if (products.Count == 0)
        {
            if (intent.Kind == DeterministicAssistantIntentKind.Alternative)
                return $"I couldn’t find a current {(IsCheaper(intent.LowerPrompt) ? "cheaper " : string.Empty)}in-stock same-category alternative to that referenced product. Try another category, budget, or product.";
            if (RequiresRecentContext(intent))
                return "The referenced selection is no longer available in the current public catalogue. Ask me to show a fresh category or name another product.";
            if (intent.Kind == DeterministicAssistantIntentKind.Recommendation)
                return "I don’t have a current in-stock recommendation for those preferences. Try a different category, budget, or intended use.";
            return "I couldn’t find a current public catalogue match. Try a different category, budget, keyword, or stock requirement.";
        }

        return intent.Kind switch
        {
            DeterministicAssistantIntentKind.Best => BestReply(products, messages, intent),
            DeterministicAssistantIntentKind.Cheapest when intent.UsesRecentContext => CheapestReply(products, messages, intent),
            DeterministicAssistantIntentKind.Availability when intent.UsesRecentContext => AvailabilityReply(products, messages, intent),
            DeterministicAssistantIntentKind.Compare => ComparisonReply(products, messages, intent),
            DeterministicAssistantIntentKind.Ordinal => DetailReply(products[0]),
            DeterministicAssistantIntentKind.Alternative => AlternativeReply(products, intent),
            DeterministicAssistantIntentKind.Recommendation => RecommendationReply(products, intent),
            DeterministicAssistantIntentKind.Cheapest => RecommendationReply(products, intent),
            _ => SearchReply(products)
        };
    }

    private static string BestReply(
        IReadOnlyList<ToolProduct> products,
        IReadOnlyList<AssistantAiMessage> messages,
        DeterministicAssistantIntent intent)
    {
        var available = products.Where(product => product.StockQuantity > 0)
            .OrderBy(product => product.Price).ToArray();
        var unavailable = products.Where(product => product.StockQuantity <= 0).ToArray();
        var reply = "There isn’t a single objective best without knowing your priority. ";
        if (available.Length > 0)
        {
            var lowestPrice = available[0].Price;
            var choices = available.Where(product => product.Price == lowestPrice).ToArray();
            reply += choices.Length == 1
                ? $"Using current availability first and current effective price second, {choices[0].Name} is the lowest-priced in-stock option at {Money(lowestPrice)}. "
                : $"Using current availability first and current effective price second, {string.Join(" and ", choices.Select(product => product.Name))} are tied as the lowest-priced in-stock options at {Money(lowestPrice)}. ";
            var otherAvailable = available.Where(product => product.Price != lowestPrice).ToArray();
            if (otherAvailable.Length > 0)
                reply += $"Other in-stock options are {string.Join(", ", otherAvailable.Select(product => product.Name))}. ";
        }
        else
        {
            reply += "None of these products is currently in stock. ";
        }
        if (unavailable.Length > 0)
            reply += $"{string.Join(", ", unavailable.Select(product => product.Name))} {(unavailable.Length == 1 ? "is" : "are")} currently out of stock. ";
        reply += "Tell me whether price, category, or intended use matters most.";
        return reply + MissingReferenceSuffix(products, messages, intent);
    }

    private static string CheapestReply(
        IReadOnlyList<ToolProduct> products,
        IReadOnlyList<AssistantAiMessage> messages,
        DeterministicAssistantIntent intent)
    {
        var lowestPrice = products.Min(product => product.Price);
        var lowest = products.Where(product => product.Price == lowestPrice).ToArray();
        string reply;
        if (lowest.Length > 1)
        {
            reply = $"{string.Join(" and ", lowest.Select(product => product.Name))} are tied for the lowest current effective price of these at {Money(lowestPrice)}. " +
                string.Join("; ", lowest.Select(product => product.StockQuantity > 0
                    ? $"{product.Name} is in stock ({product.StockQuantity} available)"
                    : $"{product.Name} is out of stock")) + ".";
            if (lowest.All(product => product.StockQuantity <= 0))
            {
                var available = products.Where(product => product.StockQuantity > 0).ToArray();
                if (available.Length > 0)
                {
                    var availablePrice = available.Min(product => product.Price);
                    var availableLowest = available.Where(product => product.Price == availablePrice).ToArray();
                    reply += availableLowest.Length == 1
                        ? $" The lowest-priced in-stock option is {availableLowest[0].Name} at {Money(availablePrice)}."
                        : $" The lowest-priced in-stock options are {string.Join(" and ", availableLowest.Select(product => product.Name))} at {Money(availablePrice)}.";
                }
            }
        }
        else if (lowest[0].StockQuantity > 0)
        {
            reply = $"{lowest[0].Name} has the lowest current effective price of these at {Money(lowestPrice)} and is in stock ({lowest[0].StockQuantity} available).";
        }
        else
        {
            reply = $"{lowest[0].Name} has the lowest current effective price of these at {Money(lowestPrice)}, but it is currently out of stock.";
            var available = products.Where(product => product.StockQuantity > 0).ToArray();
            if (available.Length > 0)
            {
                var availablePrice = available.Min(product => product.Price);
                var availableLowest = available.Where(product => product.Price == availablePrice).ToArray();
                reply += availableLowest.Length == 1
                    ? $" The lowest-priced in-stock option is {availableLowest[0].Name} at {Money(availablePrice)}."
                    : $" The lowest-priced in-stock options are {string.Join(" and ", availableLowest.Select(product => product.Name))} at {Money(availablePrice)}.";
            }
        }
        return reply + MissingReferenceSuffix(products, messages, intent);
    }

    private static string AvailabilityReply(
        IReadOnlyList<ToolProduct> products,
        IReadOnlyList<AssistantAiMessage> messages,
        DeterministicAssistantIntent intent)
    {
        var reply = "Current availability: " + string.Join("; ", products.Select(product => product.StockQuantity > 0
            ? $"{product.Name} is in stock ({product.StockQuantity} available) at {Money(product.Price)}"
            : $"{product.Name} is out of stock at {Money(product.Price)}")) + ".";
        return reply + MissingReferenceSuffix(products, messages, intent);
    }

    private static string ComparisonReply(
        IReadOnlyList<ToolProduct> products,
        IReadOnlyList<AssistantAiMessage> messages,
        DeterministicAssistantIntent intent)
    {
        var reply = "Current comparison: " + string.Join("; ", products.Select(product =>
            $"{product.Name} — {Money(product.Price)}, {(product.StockQuantity > 0 ? $"in stock ({product.StockQuantity} available)" : "out of stock")}")) + ".";
        return reply + MissingReferenceSuffix(products, messages, intent);
    }

    private static string AlternativeReply(
        IReadOnlyList<ToolProduct> products,
        DeterministicAssistantIntent intent)
    {
        return $"Current {(IsCheaper(intent.LowerPrompt) ? "cheaper " : string.Empty)}in-stock same-category alternatives to the referenced product: " +
            string.Join(", ", products.Select(product => $"{product.Name} at {Money(product.Price)}")) + ".";
    }

    private static string RecommendationReply(IReadOnlyList<ToolProduct> products, DeterministicAssistantIntent intent)
    {
        if (intent.Category is null)
            return "Here are up to three active, public, in-stock options selected deterministically by lowest current effective price: " +
                string.Join(", ", products.Select(product => $"{product.Name} at {Money(product.Price)}")) +
                ". This is a catalogue-based price and availability rule, not an objective quality ranking.";

        var list = string.Join(", ", products.Select(product =>
            $"{product.Name} at {Money(product.Price)} ({(product.StockQuantity > 0 ? $"in stock, {product.StockQuantity} available" : "out of stock")})"));
        return $"Here are current {intent.Category} catalogue options ordered by current effective price: {list}. In-stock items can be ordered now; out-of-stock items are shown only for comparison.";
    }

    private static string SearchReply(IReadOnlyList<ToolProduct> products) =>
        $"I found {products.Count} current catalogue match{(products.Count == 1 ? string.Empty : "es")}: " +
        string.Join(", ", products.Select(product => $"{product.Name} at {Money(product.Price)} ({(product.StockQuantity > 0 ? "in stock" : "out of stock")})")) + ".";

    private static string DetailReply(ToolProduct product) =>
        $"{product.Name} is currently {Money(product.Price)} in {product.CategoryName} and is " +
        (product.StockQuantity > 0 ? $"in stock ({product.StockQuantity} available)." : "out of stock.");

    private static string MissingReferenceSuffix(
        IReadOnlyList<ToolProduct> products,
        IReadOnlyList<AssistantAiMessage> messages,
        DeterministicAssistantIntent intent)
    {
        var expected = SelectReferences(intent.Selection, ReadReferences(messages)).Count;
        return expected > products.Count
            ? " One or more earlier items is no longer available in the current public catalogue."
            : string.Empty;
    }

    private static List<ToolProduct> ReadToolProducts(IReadOnlyList<AssistantAiMessage> messages)
    {
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
                        item.GetProperty("StockQuantity").GetInt32(),
                        item.TryGetProperty("CategoryName", out var category) ? category.GetString() ?? "catalogue" : "catalogue"));
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // Tool messages are produced by the controlled orchestration layer.
                // Malformed content is ignored so the mock never invents fallback facts.
            }
        }
        return products;
    }

    private static List<ProductReferenceMarker> ReadReferences(IReadOnlyList<AssistantAiMessage> messages)
    {
        var marker = messages.LastOrDefault(message => message.Role == "system" &&
            message.Content?.StartsWith("REFERENCE_PRODUCTS_JSON:", StringComparison.Ordinal) == true)?.Content;
        if (marker is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<List<ProductReferenceMarker>>(
                           marker["REFERENCE_PRODUCTS_JSON:".Length..],
                           new JsonSerializerOptions(JsonSerializerDefaults.Web))?
                       .Where(reference => reference.Position > 0).OrderBy(reference => reference.Position).ToList() ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        var legacy = messages.LastOrDefault(message => message.Role == "system" &&
            message.Content?.StartsWith("REFERENCE_PRODUCT_IDS:", StringComparison.Ordinal) == true)?.Content;
        if (legacy is null) return [];
        return legacy["REFERENCE_PRODUCT_IDS:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select((value, index) => new ProductReferenceMarker(index + 1, int.TryParse(value, out var id) && id > 0 ? id : null))
            .ToList();
    }

    private static IReadOnlyList<ProductReferenceMarker> SelectReferences(
        ReferenceSelection selection,
        IReadOnlyList<ProductReferenceMarker> references) => selection switch
    {
        ReferenceSelection.First => references.Where(reference => reference.Position == 1).Take(1).ToArray(),
        ReferenceSelection.Second => references.Where(reference => reference.Position == 2).Take(1).ToArray(),
        ReferenceSelection.Third => references.Where(reference => reference.Position == 3).Take(1).ToArray(),
        ReferenceSelection.Last => references.TakeLast(1).ToArray(),
        ReferenceSelection.FirstTwo => references.Where(reference => reference.Position is 1 or 2).Take(2).ToArray(),
        ReferenceSelection.LastTwo => references.TakeLast(2).ToArray(),
        _ => references.Take(5).ToArray()
    };

    private static bool ShouldUseReferences(DeterministicAssistantIntent intent, IReadOnlyList<ProductReferenceMarker> references) =>
        references.Count > 0 && intent.Category is null && intent.Keyword is null && intent.UsesRecentContext;

    private static bool RequiresRecentContext(DeterministicAssistantIntent intent) =>
        intent.Kind is DeterministicAssistantIntentKind.Ordinal or DeterministicAssistantIntentKind.Alternative ||
        intent.Category is null && intent.Keyword is null && intent.UsesRecentContext && (intent.Kind is
            DeterministicAssistantIntentKind.Best or DeterministicAssistantIntentKind.Compare or
            DeterministicAssistantIntentKind.Availability or DeterministicAssistantIntentKind.Cheapest);

    private static bool IsCheaper(string prompt) =>
        prompt.Contains("cheaper", StringComparison.Ordinal) || prompt.Contains("daha ucuz", StringComparison.Ordinal);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record ProductReferenceMarker(int Position, int? ProductId);
    private sealed record ToolProduct(int Id, string Name, decimal Price, int StockQuantity, string CategoryName);
}
