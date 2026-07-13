using System.Text.Json;

namespace ECommerceStore.Web.Services.Assistant.Provider;

public sealed record AssistantToolCall(string Id, string Name, JsonElement Arguments);

public sealed record AssistantAiMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    IReadOnlyList<AssistantToolCall>? ToolCalls = null);

public sealed record AssistantToolDefinition(string Name, string Description, JsonElement Parameters);

public sealed record AssistantAiRequest(
    IReadOnlyList<AssistantAiMessage> Messages,
    IReadOnlyList<AssistantToolDefinition> Tools);

public sealed record AssistantAiResponse(
    string? Content,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    string? FinishReason = null);

public interface IAssistantAiClient
{
    Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default);
}

public abstract class AssistantAiException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class AssistantAuthenticationException(string message) : AssistantAiException(message);
public sealed class AssistantTimeoutException(string message, Exception? inner = null) : AssistantAiException(message, inner);
public sealed class AssistantProviderException(int statusCode, string message, Exception? inner = null) : AssistantAiException(message, inner)
{
    public int StatusCode { get; } = statusCode;
}
public sealed class AssistantInvalidResponseException(string message, Exception? inner = null) : AssistantAiException(message, inner);
