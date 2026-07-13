using System.Text.Json.Serialization;

namespace ECommerceStore.Web.Services.Assistant.Provider;

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public List<OpenAiMessage> Messages { get; set; } = [];
    [JsonPropertyName("tools")] public List<OpenAiTool>? Tools { get; set; }
    [JsonPropertyName("tool_choice")] public string? ToolChoice { get; set; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    [JsonPropertyName("tool_calls")] public List<OpenAiToolCall>? ToolCalls { get; set; }
}

internal sealed class OpenAiTool
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiFunction Function { get; set; } = new();
}

internal sealed class OpenAiFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("parameters")] public object? Parameters { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiFunction Function { get; set; } = new();
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")] public List<OpenAiChoice>? Choices { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}
