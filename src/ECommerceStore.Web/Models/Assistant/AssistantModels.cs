using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;

namespace ECommerceStore.Web.Models.Assistant;

public enum ChatMessageRole
{
    User = 1,
    Assistant = 2
}

public enum AssistantRequestState
{
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public sealed class ChatConversation
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = "New conversation";
    public long LastSequence { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public ICollection<AssistantRequest> Requests { get; set; } = new List<AssistantRequest>();
}

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public ChatMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ChatConversation Conversation { get; set; } = null!;
    public ICollection<ChatMessageProduct> Products { get; set; } = new List<ChatMessageProduct>();
}

public sealed class ChatMessageProduct
{
    public Guid MessageId { get; set; }
    public int DisplayPosition { get; set; }
    public int? ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal? PriceAtReplyTime { get; set; }
    public ChatMessage Message { get; set; } = null!;
    public Product? Product { get; set; }
}

public sealed class AssistantRequest
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ClientRequestId { get; set; }
    public AssistantRequestState State { get; set; }
    public Guid ProcessingAttemptId { get; set; }
    public Guid UserMessageId { get; set; }
    public Guid? AssistantMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ChatConversation Conversation { get; set; } = null!;
    public ChatMessage UserMessage { get; set; } = null!;
    public ChatMessage? AssistantMessage { get; set; }
}
