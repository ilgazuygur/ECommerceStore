using ECommerceStore.Web.Services.AI;
using ECommerceStore.Web.Services.Common;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Services;

public sealed class MockAIServiceTests
{
    private static MockAIService Service(bool enabled = true) =>
        new(Options.Create(new AIServiceOptions { Enabled = enabled, Provider = "Mock" }));

    [Fact]
    public async Task Draft_is_deterministic_for_the_same_input()
    {
        var request = new AIDraftRequest("Aurora Headphones", "Electronics", "wireless, comfortable");

        var first = await Service().DraftProductDescriptionAsync(request);
        var second = await Service().DraftProductDescriptionAsync(request);

        Assert.True(first.Available);
        Assert.Equal(first.DraftText, second.DraftText);
        Assert.Contains("Aurora Headphones", first.DraftText);
        Assert.Contains("draft", first.DraftText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_service_returns_unavailable()
    {
        var service = Service(enabled: false);

        Assert.False(service.IsEnabled);
        var result = await service.DraftProductDescriptionAsync(new AIDraftRequest("Widget", null, null));
        Assert.False(result.Available);
        Assert.Equal(string.Empty, result.DraftText);
    }

    [Fact]
    public async Task Cancellation_is_honored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Service().DraftProductDescriptionAsync(new AIDraftRequest("Widget", null, null), cts.Token));
    }
}
