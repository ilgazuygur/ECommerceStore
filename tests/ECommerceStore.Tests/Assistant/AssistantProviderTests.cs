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
