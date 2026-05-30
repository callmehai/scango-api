namespace ScanGo.Api.Database.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = MessageRoles.User;
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
