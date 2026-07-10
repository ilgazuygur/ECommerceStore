using ECommerceStore.Web.Services.Common;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.AI;

public sealed record AIDraftRequest(string ProductName, string? CategoryName, string? Keywords);

public sealed record AIDraftResult(bool Available, string DraftText)
{
    public static readonly AIDraftResult Unavailable = new(false, string.Empty);
}

/// <summary>
/// Provider-independent AI seam. Core commerce flows never depend on this service; it is optional and,
/// in the shipped configuration, backed only by a deterministic no-network mock.
/// </summary>
public interface IAIService
{
    bool IsEnabled { get; }

    /// <summary>Produces an editable draft product description. Output is deterministic and clearly a draft.</summary>
    Task<AIDraftResult> DraftProductDescriptionAsync(AIDraftRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic mock implementation. It requires no API key and makes no network call.
///
/// FUTURE PROVIDER SEAM: to integrate a real provider, add a new IAIService implementation (for example
/// an "OpenAI"/"Azure" provider reading a key from user secrets or environment variables — never source
/// control) and register it in Program.cs selected by <see cref="AIServiceOptions.Provider"/>. A real
/// provider must additionally address prompt-injection, output encoding, rate limiting, cost controls,
/// timeouts/retries, and privacy of any submitted catalog text. None of that is required for the mock.
/// </summary>
public sealed class MockAIService(IOptions<AIServiceOptions> options) : IAIService
{
    private readonly AIServiceOptions _options = options.Value;

    public bool IsEnabled => _options.Enabled;

    public Task<AIDraftResult> DraftProductDescriptionAsync(AIDraftRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Enabled)
        {
            return Task.FromResult(AIDraftResult.Unavailable);
        }

        var name = string.IsNullOrWhiteSpace(request.ProductName) ? "this product" : request.ProductName.Trim();
        var category = string.IsNullOrWhiteSpace(request.CategoryName) ? "collection" : request.CategoryName.Trim();
        var keywords = string.IsNullOrWhiteSpace(request.Keywords) ? null : request.Keywords.Trim();

        // Deterministic template: the same inputs always produce the same draft, with no randomness or clock.
        var highlight = keywords is null
            ? "everyday quality and a considered design"
            : $"{keywords}";

        var draft =
            $"[AI draft — review and edit before saving] Meet {name}, a thoughtful addition to our {category}. " +
            $"Designed around {highlight}, it balances practical function with a clean, understated look. " +
            $"Use this draft as a starting point and adjust the tone, details, and specifications to match {name}.";

        return Task.FromResult(new AIDraftResult(true, draft));
    }
}
