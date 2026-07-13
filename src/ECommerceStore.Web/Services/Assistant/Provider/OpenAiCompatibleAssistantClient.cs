using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ECommerceStore.Web.Services.Common;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Web.Services.Assistant.Provider;

public sealed class OpenAiCompatibleAssistantClient(HttpClient httpClient, IOptions<AssistantOptions> options) : IAssistantAiClient
{
    private const int MaximumResponseBytes = 1_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private readonly AssistantOptions _options = options.Value;

    public async Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderComplete)
            throw new AssistantProviderException(0, "The assistant provider is not completely configured.");

        var body = new OpenAiChatRequest
        {
            Model = _options.Model,
            Messages = request.Messages.Select(ToWireMessage).ToList(),
            Tools = request.Tools.Count == 0 ? null : request.Tools.Select(tool => new OpenAiTool
            {
                Function = new OpenAiFunction
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = tool.Parameters
                }
            }).ToList(),
            ToolChoice = request.Tools.Count == 0 ? null : "auto"
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint())
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new AssistantTimeoutException("The assistant provider did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AssistantProviderException(0, "The assistant provider could not be reached.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AssistantAuthenticationException("The assistant provider rejected its configured credentials.");
            if (!response.IsSuccessStatusCode)
                throw new AssistantProviderException((int)response.StatusCode, $"The assistant provider returned HTTP {(int)response.StatusCode}.");

            string json;
            try
            {
                json = await ReadBoundedBodyAsync(response.Content, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw new AssistantTimeoutException("The assistant provider did not finish its response in time.", ex);
            }

            return ParseResponse(json);
        }
    }

    private Uri BuildEndpoint() => new($"{_options.BaseUrl.TrimEnd('/')}/chat/completions", UriKind.Absolute);

    private static async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
            throw new AssistantInvalidResponseException("The assistant provider response exceeded the safe size limit.");

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > MaximumResponseBytes)
                throw new AssistantInvalidResponseException("The assistant provider response exceeded the safe size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(destination.GetBuffer(), 0, checked((int)destination.Length));
    }

    private static OpenAiMessage ToWireMessage(AssistantAiMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        ToolCallId = message.ToolCallId,
        ToolCalls = message.ToolCalls?.Select(call => new OpenAiToolCall
        {
            Id = call.Id,
            Function = new OpenAiFunction { Name = call.Name, Arguments = call.Arguments.GetRawText() }
        }).ToList()
    };

    private static AssistantAiResponse ParseResponse(string json)
    {
        try
        {
            var choice = JsonSerializer.Deserialize<OpenAiChatResponse>(json, JsonOptions)?.Choices?.FirstOrDefault();
            if (choice?.Message is null)
                throw new AssistantInvalidResponseException("The assistant provider returned no response choice.");

            var calls = new List<AssistantToolCall>();
            foreach (var call in choice.Message.ToolCalls ?? [])
            {
                if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Function.Name) || string.IsNullOrWhiteSpace(call.Function.Arguments))
                    throw new AssistantInvalidResponseException("The assistant provider returned a malformed tool call.");
                using var arguments = JsonDocument.Parse(call.Function.Arguments);
                calls.Add(new AssistantToolCall(call.Id, call.Function.Name, arguments.RootElement.Clone()));
            }

            if (calls.Count == 0 && string.IsNullOrWhiteSpace(choice.Message.Content))
                throw new AssistantInvalidResponseException("The assistant provider returned neither text nor a tool call.");
            return new AssistantAiResponse(choice.Message.Content, calls, choice.FinishReason);
        }
        catch (JsonException ex)
        {
            throw new AssistantInvalidResponseException("The assistant provider returned malformed JSON.", ex);
        }
    }
}
