namespace ScanGo.Api.Database.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Topic { get; set; } = ConversationTopics.General;
    public string RootLang { get; set; } = "auto";
    public string TargetLang { get; set; } = "vnm";
    public string? ImageStorageKey { get; set; }      // R2 object key
    public string? ImageMimeType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
}
