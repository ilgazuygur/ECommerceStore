using System.Net;
using System.Text;
using System.Text.Json;
using ECommerceStore.Web.Services.Assistant.Provider;
using ECommerceStore.Web.Services.Common;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Assistant;

public sealed class AssistantProviderTests
{
    private const string Secret = "secret-that-must-never-leak";

    [Fact]
    public async Task Serializes_tools_and_parses_tool_call_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"SearchProducts","arguments":"{\"keyword\":\"lamp\"}"}}]},"finish_reason":"tool_calls"}]}""");
        var client = Create(handler);
        using var schema = JsonDocument.Parse("""{"type":"object"}""");

        var response = await client.CompleteAsync(new AssistantAiRequest(
            [new AssistantAiMessage("user", "Find a lamp")],
            [new AssistantToolDefinition("SearchProducts", "Search", schema.RootElement.Clone())]));

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("SearchProducts", call.Name);
        Assert.Equal("lamp", call.Arguments.GetProperty("keyword").GetString());
        Assert.Contains("\"tools\"", handler.RequestBody);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(Secret, handler.AuthorizationParameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Authentication_failures_do_not_leak_key(HttpStatusCode status)
    {
        var client = Create(new StubHandler(status, Secret));
        var error = await Assert.ThrowsAsync<AssistantAuthenticationException>(() => client.CompleteAsync(EmptyRequest()));
        Assert.DoesNotContain(Secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_provider_response_is_rejected()
    {
        var client = Create(new StubHandler(HttpStatusCode.OK, "not-json"));
        await Assert.ThrowsAsync<AssistantInvalidResponseException>(() => client.CompleteAsync(EmptyRequest()));
    }

    [Fact]
    public async Task Oversized_provider_response_is_rejected_without_key_leakage()
    {
        var client = Create(new StubHandler(HttpStatusCode.OK, new string('x', 1_000_001)));
        var error = await Assert.ThrowsAsync<AssistantInvalidResponseException>(() => client.CompleteAsync(EmptyRequest()));
        Assert.DoesNotContain(Secret, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://provider.test/v1?key=unsafe")]
    [InlineData("https://user:password@provider.test/v1")]
    [InlineData("http://provider.test/v1")]
    public void Real_provider_configuration_rejects_unsafe_base_url(string baseUrl)
    {
        var options = new AssistantOptions { Provider = "OpenAICompatible", BaseUrl = baseUrl, Model = "model", ApiKey = Secret };
        Assert.False(options.IsRealProviderComplete);
    }

    [Fact]
    public async Task Provider_timeout_and_caller_cancellation_are_distinct()
    {
        var timeoutClient = Create(new StubHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }), timeoutSeconds: 0.01);
        await Assert.ThrowsAsync<AssistantTimeoutException>(() => timeoutClient.CompleteAsync(EmptyRequest()));

        var cancellationClient = Create(new StubHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellationClient.CompleteAsync(EmptyRequest(), cts.Token));
    }

    [Fact]
    public async Task Deterministic_mock_requests_a_controlled_tool()
    {
        var mock = new MockAssistantAiClient();
        var first = await mock.CompleteAsync(new AssistantAiRequest([new AssistantAiMessage("user", "electronics under $100")], []));
        var second = await mock.CompleteAsync(new AssistantAiRequest([new AssistantAiMessage("user", "electronics under $100")], []));
        Assert.Equal(first.ToolCalls[0].Name, second.ToolCalls[0].Name);
        Assert.Equal(first.ToolCalls[0].Arguments.GetRawText(), second.ToolCalls[0].Arguments.GetRawText());
    }

    [Theory]
    [InlineData("the first one", "11")]
    [InlineData("the second one", "22")]
    [InlineData("the last one", "33")]
    public async Task Mock_resolves_ordinal_references_deterministically(string prompt, string expectedId)
    {
        var response = await new MockAssistantAiClient().CompleteAsync(new AssistantAiRequest(
            [new AssistantAiMessage("system", "REFERENCE_PRODUCT_IDS:11,22,33"), new AssistantAiMessage("user", prompt)], []));
        Assert.Equal(expectedId, response.ToolCalls[0].Arguments.GetProperty("productId").GetRawText());
    }

    [Fact]
    public async Task Mock_resolves_last_two_and_grounds_price_comparison_in_tool_results()
    {
        var mock = new MockAssistantAiClient();
        var first = await mock.CompleteAsync(new AssistantAiRequest(
            [new AssistantAiMessage("system", "REFERENCE_PRODUCT_IDS:11,22,33"), new AssistantAiMessage("user", "Compare the last two")], []));
        Assert.Equal(new[] { 22, 33 }, first.ToolCalls.Select(call => call.Arguments.GetProperty("productId").GetInt32()));

        var final = await mock.CompleteAsync(new AssistantAiRequest(
            [
                new AssistantAiMessage("user", "Which of those is cheaper?"),
                new AssistantAiMessage("tool", "[{\"Id\":22,\"Name\":\"Alpha\",\"EffectivePrice\":50,\"StockQuantity\":2}]", "one"),
                new AssistantAiMessage("tool", "[{\"Id\":33,\"Name\":\"Beta\",\"EffectivePrice\":40,\"StockQuantity\":3}]", "two")
            ], []));
        Assert.Contains("Beta", final.Content);
        Assert.Contains("40.00", final.Content);
    }

    [Theory]
    [InlineData("What should I buy?")]
    [InlineData("What do you recommend?")]
    [InlineData("Recommend something")]
    [InlineData("Show me something useful")]
    [InlineData("What should I order?")]
    [InlineData("Bana ne önerirsin?")]
    [InlineData("Ne almalıyım?")]
    [InlineData("Bir şey öner")]
    [InlineData("Hangi ürünü almalıyım?")]
    public async Task Mock_routes_general_English_and_Turkish_recommendations_to_a_small_controlled_search(string prompt)
    {
        var response = await CompleteMockAsync(prompt);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("GetInStockProducts", call.Name);
        Assert.InRange(call.Arguments.GetProperty("limit").GetInt32(), 1, 4);
        Assert.Equal(JsonValueKind.Null, call.Arguments.GetProperty("keyword").ValueKind);
    }

    [Fact]
    public async Task Mock_recommendation_with_budget_uses_a_bounded_in_stock_price_search()
    {
        var response = await CompleteMockAsync("What should I buy under $70?");

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("SearchProducts", call.Name);
        Assert.Equal(0m, call.Arguments.GetProperty("minimumPrice").GetDecimal());
        Assert.Equal(70m, call.Arguments.GetProperty("maximumPrice").GetDecimal());
        Assert.True(call.Arguments.GetProperty("inStockOnly").GetBoolean());
        Assert.InRange(call.Arguments.GetProperty("limit").GetInt32(), 1, 3);
    }

    [Fact]
    public async Task Mock_general_recommendation_final_reply_explains_its_grounded_policy()
    {
        var response = await CompleteMockAsync("what should i buy", null,
            """[{"Id":11,"Name":"Alpha","EffectivePrice":28,"StockQuantity":4,"CategoryName":"accessories"},{"Id":22,"Name":"Beta","EffectivePrice":35,"StockQuantity":2,"CategoryName":"fitness"}]""");

        Assert.Empty(response.ToolCalls);
        Assert.Contains("active, public, in-stock options", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lowest current effective price", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alpha at 28.00", response.Content);
        Assert.DoesNotContain("couldn’t find", response.Content, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsupportedClaims(response.Content);
    }

    [Theory]
    [InlineData("Recommend electronics", "electronics")]
    [InlineData("Elektronik bir şey öner", "electronics")]
    [InlineData("What should I buy for fitness?", "fitness")]
    [InlineData("Spor için ne almalıyım?", "fitness")]
    public async Task Mock_routes_category_recommendations_without_broad_keyword_search(string prompt, string category)
    {
        var response = await CompleteMockAsync(prompt);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("GetProductsByCategory", call.Name);
        Assert.Equal(category, call.Arguments.GetProperty("category").GetString());
        Assert.InRange(call.Arguments.GetProperty("limit").GetInt32(), 1, 4);
    }

    [Theory]
    [InlineData("f")]
    [InlineData("d")]
    [InlineData("!!!")]
    [InlineData("f f f")]
    [InlineData("dddddd")]
    [InlineData("f please")]
    [InlineData("d product")]
    [InlineData("™")]
    [InlineData("™™™")]
    public async Task Mock_rejects_meaningless_input_without_calling_a_catalogue_tool(string prompt)
    {
        var response = await CompleteMockAsync(prompt);

        Assert.Empty(response.ToolCalls);
        Assert.Contains("product, category, budget", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("I found", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TV", "tv")]
    [InlineData("PC", "pc")]
    [InlineData("4K", "4k")]
    public async Task Mock_preserves_valid_short_commercial_search_terms(string prompt, string expectedKeyword)
    {
        var response = await CompleteMockAsync(prompt);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("SearchProducts", call.Name);
        Assert.Equal(expectedKeyword, call.Arguments.GetProperty("keyword").GetString());
    }

    [Theory]
    [InlineData("phone in stock", "GetInStockProducts", "phone")]
    [InlineData("which phone is in stock", "GetInStockProducts", "phone")]
    [InlineData("cheapest TV", "SearchProducts", "tv")]
    public async Task Mock_explicit_product_terms_replace_recent_context(
        string prompt,
        string expectedTool,
        string expectedKeyword)
    {
        var response = await CompleteMockAsync(prompt, [11, 22, 33]);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal(expectedTool, call.Name);
        Assert.Equal(expectedKeyword, call.Arguments.GetProperty("keyword").GetString());
    }

    [Theory]
    [InlineData("Which one is best?", "11,22,33")]
    [InlineData("Hangisi daha iyi?", "11,22,33")]
    [InlineData("HANGİSİ DAHA İYİ?", "11,22,33")]
    [InlineData("Which of those is cheaper?", "11,22,33")]
    [InlineData("Bunlardan hangisi daha ucuz?", "11,22,33")]
    [InlineData("Are those in stock?", "11,22,33")]
    [InlineData("Bunlar stokta mı?", "11,22,33")]
    [InlineData("Compare the first two", "11,22")]
    [InlineData("İlk ikisini karşılaştır", "11,22")]
    [InlineData("Compare the last two", "22,33")]
    [InlineData("Son ikisini karşılaştır", "22,33")]
    public async Task Mock_resolves_collective_recent_references_in_English_and_Turkish(
        string prompt,
        string expectedIds)
    {
        var response = await CompleteMockAsync(prompt, [11, 22, 33]);

        Assert.Equal(
            expectedIds.Split(',').Select(int.Parse),
            response.ToolCalls.Select(call => call.Arguments.GetProperty("productId").GetInt32()));
        Assert.All(response.ToolCalls, call => Assert.Equal("GetProductDetails", call.Name));
    }

    [Theory]
    [InlineData("the first one", 11)]
    [InlineData("the second one", 22)]
    [InlineData("the third one", 33)]
    [InlineData("the last one", 44)]
    [InlineData("birincisi nasıl?", 11)]
    [InlineData("ikincisi nasıl?", 22)]
    [InlineData("üçüncüsü nasıl?", 33)]
    [InlineData("sonuncusu nasıl?", 44)]
    public async Task Mock_resolves_English_and_Turkish_ordinal_references(string prompt, int expectedId)
    {
        var response = await CompleteMockAsync(prompt, [11, 22, 33, 44]);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("GetProductDetails", call.Name);
        Assert.Equal(expectedId, call.Arguments.GetProperty("productId").GetInt32());
    }

    [Theory]
    [InlineData("Show me fitness products", "fitness")]
    [InlineData("Elektronik ürünleri göster", "electronics")]
    public async Task Mock_explicit_new_category_replaces_recent_reference_context(string prompt, string category)
    {
        var response = await CompleteMockAsync(prompt, [11, 22, 33]);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("GetProductsByCategory", call.Name);
        Assert.Equal(category, call.Arguments.GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("Which one is best?")]
    [InlineData("Compare the first two")]
    [InlineData("ikincisi nasıl?")]
    [InlineData("Are those in stock?")]
    public async Task Mock_follow_up_without_recent_references_returns_guidance_instead_of_a_broad_search(string prompt)
    {
        var response = await CompleteMockAsync(prompt);

        Assert.Empty(response.ToolCalls);
        Assert.Contains("recent product set", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mock_alternative_to_second_uses_scoped_grounded_alternative_lookup()
    {
        int[] references = [11, 22, 33, 44, 55];

        var alternativeRequest = await CompleteMockAsync("Show me an alternative to the second one", references);

        var call = Assert.Single(alternativeRequest.ToolCalls);
        Assert.Equal("GetProductAlternatives", call.Name);
        Assert.Equal(22, call.Arguments.GetProperty("productId").GetInt32());
        Assert.Equal([11, 33, 44, 55], call.Arguments.GetProperty("candidateProductIds")
            .EnumerateArray().Select(item => item.GetInt32()));
        Assert.False(call.Arguments.GetProperty("cheaperOnly").GetBoolean());

        var final = await CompleteMockAsync("Show me an alternative to the second one", references,
            """[{"Id":11,"Name":"Alpha","EffectivePrice":70,"StockQuantity":4,"CategoryName":"electronics"},{"Id":55,"Name":"Epsilon","EffectivePrice":120,"StockQuantity":5,"CategoryName":"electronics"}]""");

        Assert.Contains("in-stock same-category alternatives", final.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alpha at 70.00", final.Content);
        Assert.Contains("Epsilon at 120.00", final.Content);
        Assert.DoesNotContain("Beta at", final.Content, StringComparison.OrdinalIgnoreCase);

        var cheaperRequest = await CompleteMockAsync("Show me a cheaper alternative to the second one", references);
        var cheaperCall = Assert.Single(cheaperRequest.ToolCalls);
        Assert.Equal("GetProductAlternatives", cheaperCall.Name);
        Assert.True(cheaperCall.Arguments.GetProperty("cheaperOnly").GetBoolean());

        var cheaper = await CompleteMockAsync("Show me a cheaper alternative to the second one", references,
            """[{"Id":11,"Name":"Alpha","EffectivePrice":70,"StockQuantity":4,"CategoryName":"electronics"}]""");

        Assert.Contains("Alpha at 70.00", cheaper.Content);
        Assert.DoesNotContain("Epsilon at", cheaper.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mock_structured_reference_gap_never_reinterprets_position_three_as_the_second_product()
    {
        var response = await new MockAssistantAiClient().CompleteAsync(new AssistantAiRequest(
        [
            new AssistantAiMessage("system",
                """REFERENCE_PRODUCTS_JSON:[{"position":1,"productId":11},{"position":2,"productId":null},{"position":3,"productId":33}]"""),
            new AssistantAiMessage("user", "the second one")
        ], []));

        Assert.Empty(response.ToolCalls);
        Assert.Contains("no longer available", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("33", response.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mock_best_reply_is_qualified_and_uses_only_live_price_and_stock_tool_facts()
    {
        var response = await CompleteMockAsync("Which one is best?", [11, 22, 33],
            """[{"Id":11,"Name":"Alpha","EffectivePrice":45,"StockQuantity":0,"CategoryName":"electronics"}]""",
            """[{"Id":22,"Name":"Beta","EffectivePrice":65,"StockQuantity":4,"CategoryName":"electronics"}]""",
            """[{"Id":33,"Name":"Gamma","EffectivePrice":55,"StockQuantity":2,"CategoryName":"electronics"}]""");

        Assert.Empty(response.ToolCalls);
        Assert.Contains("isn’t a single objective best", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gamma", response.Content);
        Assert.Contains("55.00", response.Content);
        Assert.Contains("Alpha", response.Content);
        Assert.Contains("out of stock", response.Content, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsupportedClaims(response.Content);
    }

    [Fact]
    public async Task Mock_cheapest_reply_distinguishes_live_price_from_availability()
    {
        var response = await CompleteMockAsync("Which of those is cheaper?", [11, 22],
            """[{"Id":11,"Name":"Alpha","EffectivePrice":40,"StockQuantity":0,"CategoryName":"electronics"}]""",
            """[{"Id":22,"Name":"Beta","EffectivePrice":52.5,"StockQuantity":7,"CategoryName":"electronics"}]""");

        Assert.Contains("Alpha", response.Content);
        Assert.Contains("40.00", response.Content);
        Assert.Contains("out of stock", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lowest-priced in-stock option is Beta", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("52.50", response.Content);
        AssertNoUnsupportedClaims(response.Content);
    }

    [Fact]
    public async Task Mock_reports_equal_price_ties_without_using_product_id_as_a_quality_tiebreaker()
    {
        var best = await CompleteMockAsync("Which one is best?", [11, 22, 33],
            """[{"Id":11,"Name":"Alpha","EffectivePrice":50,"StockQuantity":2,"CategoryName":"electronics"}]""",
            """[{"Id":22,"Name":"Beta","EffectivePrice":50,"StockQuantity":3,"CategoryName":"electronics"}]""",
            """[{"Id":33,"Name":"Gamma","EffectivePrice":70,"StockQuantity":4,"CategoryName":"electronics"}]""");
        var cheapest = await CompleteMockAsync("Which one is cheapest?", [11, 22, 33],
            """[{"Id":11,"Name":"Alpha","EffectivePrice":40,"StockQuantity":0,"CategoryName":"electronics"}]""",
            """[{"Id":22,"Name":"Beta","EffectivePrice":40,"StockQuantity":2,"CategoryName":"electronics"}]""",
            """[{"Id":33,"Name":"Gamma","EffectivePrice":70,"StockQuantity":4,"CategoryName":"electronics"}]""");

        Assert.Contains("Alpha and Beta are tied as the lowest-priced in-stock options", best.Content);
        Assert.Contains("Alpha and Beta are tied for the lowest current effective price", cheapest.Content);
        Assert.Contains("Alpha is out of stock", cheapest.Content);
        Assert.Contains("Beta is in stock (2 available)", cheapest.Content);
        AssertNoUnsupportedClaims(best.Content);
        AssertNoUnsupportedClaims(cheapest.Content);
    }

    [Fact]
    public async Task Mock_availability_reply_reports_each_live_tool_result_without_invented_claims()
    {
        var response = await CompleteMockAsync("Are those in stock?", [11, 22],
            """[{"Id":11,"Name":"Alpha","EffectivePrice":41.25,"StockQuantity":3,"CategoryName":"electronics"}]""",
            """[{"Id":22,"Name":"Beta","EffectivePrice":59,"StockQuantity":0,"CategoryName":"electronics"}]""");

        Assert.Contains("Alpha is in stock (3 available) at 41.25", response.Content);
        Assert.Contains("Beta is out of stock at 59.00", response.Content);
        AssertNoUnsupportedClaims(response.Content);
    }

    [Fact]
    public async Task Mock_comparison_reply_preserves_live_price_and_stock_for_each_selected_product()
    {
        var response = await CompleteMockAsync("Compare the first two", [11, 22, 33],
            """[{"Id":11,"Name":"Alpha","EffectivePrice":71.2,"StockQuantity":8,"CategoryName":"electronics"}]""",
            """[{"Id":22,"Name":"Beta","EffectivePrice":64,"StockQuantity":0,"CategoryName":"electronics"}]""");

        Assert.Contains("Alpha — 71.20, in stock (8 available)", response.Content);
        Assert.Contains("Beta — 64.00, out of stock", response.Content);
        Assert.DoesNotContain("Gamma", response.Content);
        AssertNoUnsupportedClaims(response.Content);
    }

    private static Task<AssistantAiResponse> CompleteMockAsync(
        string prompt,
        int[]? referenceIds = null,
        params string[] toolResults)
    {
        var messages = new List<AssistantAiMessage>();
        if (referenceIds is not null)
            messages.Add(new AssistantAiMessage("system", $"REFERENCE_PRODUCT_IDS:{string.Join(',', referenceIds)}"));
        messages.Add(new AssistantAiMessage("user", prompt));
        messages.AddRange(toolResults.Select((result, index) => new AssistantAiMessage("tool", result, $"call-{index + 1}")));
        return new MockAssistantAiClient().CompleteAsync(new AssistantAiRequest(messages, []));
    }

    private static void AssertNoUnsupportedClaims(string? content)
    {
        Assert.NotNull(content);
        Assert.DoesNotContain("rating", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("review", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("popularity", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bestseller", content, StringComparison.OrdinalIgnoreCase);
    }

    private static AssistantAiRequest EmptyRequest() => new([new AssistantAiMessage("user", "hello")], []);

    private static OpenAiCompatibleAssistantClient Create(StubHandler handler, double timeoutSeconds = 30) =>
        new(new HttpClient(handler), Options.Create(new AssistantOptions
        {
            Provider = "OpenAICompatible", BaseUrl = "https://provider.test/v1", Model = "test-model",
            ApiKey = Secret, TimeoutSeconds = timeoutSeconds < 1 ? 1 : (int)timeoutSeconds
        }));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public string RequestBody { get; private set; } = string.Empty;
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        public StubHandler(HttpStatusCode status, string body) : this((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return await _send(request, cancellationToken);
        }
    }
}
