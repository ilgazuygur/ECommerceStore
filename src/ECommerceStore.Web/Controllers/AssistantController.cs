using System.Security.Claims;
using ECommerceStore.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerceStore.Web.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("assistant")]
[Route("api/assistant")]
public sealed class AssistantController(IShoppingAssistantService assistant) : ControllerBase
{
    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<AssistantConversationSummaryDto>>> List(CancellationToken token) =>
        Ok(await assistant.ListConversationsAsync(UserId(), token));

    [HttpPost("conversations")]
    public async Task<ActionResult<AssistantConversationDto>> Create(CancellationToken token)
    {
        var conversation = await assistant.CreateConversationAsync(UserId(), token);
        return CreatedAtAction(nameof(Get), new { conversationId = conversation.Id }, conversation);
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<ActionResult<AssistantConversationDto>> Get(Guid conversationId, CancellationToken token)
    {
        var conversation = await assistant.GetConversationAsync(UserId(), conversationId, token);
        return conversation is null ? NotFoundProblem() : Ok(conversation);
    }

    [HttpDelete("conversations/{conversationId:guid}")]
    public async Task<IActionResult> Delete(Guid conversationId, CancellationToken token) =>
        await assistant.DeleteConversationAsync(UserId(), conversationId, token) ? NoContent() : NotFoundProblem();

    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> Send(Guid conversationId, SendAssistantMessageRequest input, CancellationToken token)
    {
        if (input.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.Message))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid assistant message", detail: "A message and clientRequestId are required.");

        var result = await assistant.SendAsync(UserId(), conversationId, input.ClientRequestId, input.Message, token);
        return result.Status switch
        {
            AssistantSendStatus.Completed => Ok(result),
            AssistantSendStatus.Processing => Accepted(value: new { status = "processing", retryAfterMilliseconds = 800 }),
            AssistantSendStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Idempotency conflict", detail: result.ErrorMessage),
            _ when result.ErrorCode == "not_found" => NotFoundProblem(),
            _ when result.ErrorCode == "validation" => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid assistant message", detail: result.ErrorMessage),
            _ when result.ErrorCode is "provider_timeout" or "provider_authentication" or "provider_unavailable" or "invalid_tool_call" =>
                Problem(statusCode: StatusCodes.Status502BadGateway, title: "Assistant unavailable", detail: result.ErrorMessage),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Assistant request failed", detail: "The request could not be completed. You can safely retry.")
        };
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user id was missing.");

    private ObjectResult NotFoundProblem() => Problem(statusCode: StatusCodes.Status404NotFound, title: "Conversation not found");
}

public sealed class SendAssistantMessageRequest
{
    public Guid ClientRequestId { get; set; }
    public string Message { get; set; } = string.Empty;
}
