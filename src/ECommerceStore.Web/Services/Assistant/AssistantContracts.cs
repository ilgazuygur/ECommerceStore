namespace ECommerceStore.Web.Services.Assistant;

public sealed record AssistantConversationSummaryDto(Guid Id, string Title, DateTime UpdatedAtUtc);

public sealed record AssistantProductCardDto(
    int? ProductId,
    string Name,
    decimal? Price,
    decimal? NormalPrice,
    int? StockQuantity,
    string? Category,
    string? ImageUrl,
    string? ProductUrl,
    bool IsUnavailable,
    string CurrencyCode);

public sealed record AssistantMessageDto(
    Guid Id,
    string Role,
    string Content,
    long Sequence,
    DateTime CreatedAtUtc,
    IReadOnlyList<AssistantProductCardDto> Products);

public sealed record AssistantConversationDto(
    Guid Id,
    string Title,
    DateTime UpdatedAtUtc,
    IReadOnlyList<AssistantMessageDto> Messages);

public enum AssistantSendStatus
{
    Completed,
    Processing,
    Conflict,
    Failed
}

public sealed record AssistantSendResult(
    AssistantSendStatus Status,
    AssistantMessageDto? Message = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IShoppingAssistantService
{
    Task<IReadOnlyList<AssistantConversationSummaryDto>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default);
    Task<AssistantConversationDto> CreateConversationAsync(string userId, CancellationToken cancellationToken = default);
    Task<AssistantConversationDto?> GetConversationAsync(string userId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(string userId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<AssistantSendResult> SendAsync(string userId, Guid conversationId, Guid clientRequestId, string prompt, CancellationToken cancellationToken = default);
}
