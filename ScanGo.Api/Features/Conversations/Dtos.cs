using System.ComponentModel.DataAnnotations;

namespace ScanGo.Api.Features.Conversations;

public class ConversationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Topic { get; set; } = "";
    public string RootLang { get; set; } = "";
    public string TargetLang { get; set; } = "";
    public bool HasImage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MessageDto
{
    public Guid Id { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class ConversationDetailDto : ConversationDto
{
    public List<MessageDto> Messages { get; set; } = [];
}

public class HistoryPageDto
{
    public List<ConversationDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Limit { get; set; }
    public bool HasMore { get; set; }
}

public class RenameConversationRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = "";
}
