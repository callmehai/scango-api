using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Ai;
using ScanGo.Api.Features.Metering;
using ScanGo.Api.Features.Ocr;
using ScanGo.Api.Features.Storage;
using ScanGo.Api.Features.Tts;

namespace ScanGo.Api.Features.Conversations;

public enum ConversationError
{
    NotFound,
    InvalidTopic,
    InvalidImage,
    TitleTooLong,
    TitleEmpty,
    NoImage,
    AlreadyAnswered,
}

public record ScanStreamChunk(string Delta);
public record AskStreamChunk(string Delta);

public interface IConversationService
{
    Task<(ConversationDto? dto, ConversationError? err)> CreateScanAsync(
        Guid userId,
        Stream imageStream,
        string contentType,
        string topic,
        string rootLang,
        string targetLang,
        CancellationToken ct);

    Task<HistoryPageDto> ListAsync(
        Guid userId,
        int skip,
        int limit,
        string? topic,
        string? q,
        CancellationToken ct);

    Task<ConversationDetailDto?> GetAsync(Guid userId, Guid conversationId, CancellationToken ct);

    Task<(Stream stream, string contentType)?> GetImageAsync(
        Guid userId, Guid conversationId, CancellationToken ct);

    Task<ConversationError?> RenameAsync(
        Guid userId, Guid conversationId, string newTitle, CancellationToken ct);

    Task<ConversationError?> DeleteAsync(
        Guid userId, Guid conversationId, CancellationToken ct);

    /// <summary>
    /// OCR the stored image, ask Gemini to describe it, stream chunks back,
    /// persist the assistant message + extracted title. Yields raw text deltas.
    /// </summary>
    IAsyncEnumerable<string> ScanStreamAsync(
        Guid userId, Guid conversationId, CancellationToken ct);

    /// <summary>
    /// Add a user follow-up question, stream assistant reply, persist both
    /// user + assistant messages.
    /// </summary>
    IAsyncEnumerable<string> AskStreamAsync(
        Guid userId, Guid conversationId, string question, CancellationToken ct);

    /// <summary>
    /// Synthesize <paramref name="text"/> to MP3 using the conversation's target
    /// language voice. found=false when the conversation doesn't exist / isn't the
    /// user's; audio=null when TTS isn't configured (caller → 503).
    /// </summary>
    Task<(byte[]? audio, bool found)> SpeakAsync(
        Guid userId, Guid conversationId, string text, CancellationToken ct);
}

public class ConversationService(
    ScanGoDbContext db,
    IObjectStorage storage,
    IOcrService ocr,
    IGeminiService gemini,
    IQuotaService quota,
    ITtsService tts) : IConversationService
{
    public const int MaxTitleLength = 200;
    public const long MaxUploadBytes = 10 * 1024 * 1024;     // 10 MB

    public async Task<(ConversationDto?, ConversationError?)> CreateScanAsync(
        Guid userId,
        Stream imageStream,
        string contentType,
        string topic,
        string rootLang,
        string targetLang,
        CancellationToken ct)
    {
        if (!ConversationTopics.All.Contains(topic))
            return (null, ConversationError.InvalidTopic);

        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (null, ConversationError.InvalidImage);

        MemoryStream optimised;
        try
        {
            optimised = await ImageProcessor.OptimiseForStorageAsync(imageStream, ct);
        }
        catch (Exception)
        {
            return (null, ConversationError.InvalidImage);
        }

        var conversation = new Conversation
        {
            UserId = userId,
            Title = "",                                    // filled in by AI later
            Topic = topic,
            RootLang = rootLang,
            TargetLang = targetLang,
            ImageMimeType = "image/jpeg",
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        var storageKey = StorageKey(userId, conversation.Id);
        conversation.ImageStorageKey = storageKey;

        try
        {
            await storage.PutAsync(storageKey, optimised, "image/jpeg", ct);
        }
        finally
        {
            await optimised.DisposeAsync();
        }
        await db.SaveChangesAsync(ct);

        return (ToDto(conversation), null);
    }

    public async Task<HistoryPageDto> ListAsync(
        Guid userId,
        int skip,
        int limit,
        string? topic,
        string? q,
        CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        limit = Math.Clamp(limit, 1, 100);

        var query = db.Conversations.AsNoTracking().Where(c => c.UserId == userId);

        if (!string.IsNullOrWhiteSpace(topic) && ConversationTopics.All.Contains(topic))
            query = query.Where(c => c.Topic == topic);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(c => c.Title.ToLower().Contains(needle));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(skip).Take(limit)
            .ToListAsync(ct);

        return new HistoryPageDto
        {
            Items = rows.Select(ToDto).ToList(),
            Total = total,
            Skip = skip,
            Limit = limit,
            HasMore = skip + rows.Count < total,
        };
    }

    public async Task<ConversationDetailDto?> GetAsync(
        Guid userId, Guid conversationId, CancellationToken ct)
    {
        var convo = await db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (convo is null) return null;

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
            })
            .ToListAsync(ct);

        var dto = (ConversationDetailDto)ToDetailDto(convo);
        dto.Messages = messages;
        return dto;
    }

    public async Task<(Stream stream, string contentType)?> GetImageAsync(
        Guid userId, Guid conversationId, CancellationToken ct)
    {
        var convo = await db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (convo?.ImageStorageKey is null) return null;
        return await storage.GetAsync(convo.ImageStorageKey, ct);
    }

    public async Task<ConversationError?> RenameAsync(
        Guid userId, Guid conversationId, string newTitle, CancellationToken ct)
    {
        var trimmed = (newTitle ?? "").Trim();
        if (trimmed.Length == 0) return ConversationError.TitleEmpty;
        if (trimmed.Length > MaxTitleLength) return ConversationError.TitleTooLong;

        var rows = await db.Conversations
            .Where(c => c.Id == conversationId && c.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Title, trimmed)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);
        return rows > 0 ? null : ConversationError.NotFound;
    }

    public async Task<(byte[]? audio, bool found)> SpeakAsync(
        Guid userId, Guid conversationId, string text, CancellationToken ct)
    {
        // Scope to the owner; project just the target language. null => no row.
        var targetLang = await db.Conversations
            .Where(c => c.Id == conversationId && c.UserId == userId)
            .Select(c => c.TargetLang)
            .FirstOrDefaultAsync(ct);
        if (targetLang is null) return (null, false);

        var audio = await tts.SynthesizeAsync(text, targetLang, ct);
        return (audio, true);
    }

    public async Task<ConversationError?> DeleteAsync(
        Guid userId, Guid conversationId, CancellationToken ct)
    {
        var convo = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (convo is null) return ConversationError.NotFound;

        if (convo.ImageStorageKey is not null)
            await storage.DeleteAsync(convo.ImageStorageKey, ct);

        db.Conversations.Remove(convo);            // cascade drops messages
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async IAsyncEnumerable<string> ScanStreamAsync(
        Guid userId, Guid conversationId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var convo = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (convo is null) throw new InvalidOperationException("Conversation not found");
        if (convo.ImageStorageKey is null) throw new InvalidOperationException("No image");

        var got = await storage.GetAsync(convo.ImageStorageKey, ct);
        if (got is null) throw new InvalidOperationException("Image missing in storage");

        string ocrText;
        await using (var imgStream = got.Value.stream)
        {
            var ocrResult = await ocr.ExtractTextAsync(imgStream, convo.RootLang, ct);
            ocrText = ocrResult.Text;
        }

        var prompt = Prompts.ForScan(ocrText, convo.TargetLang, convo.Topic);

        var usage = new UsageBox();
        var full = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in gemini.StreamAsync(prompt, usage, ct))
            {
                full.Append(chunk);
                yield return chunk;
            }
        }
        finally
        {
            // Persist whatever streamed — even if the client disconnected mid-stream
            // (ct cancelled) or Gemini errored partway. Uses a non-cancellable token so
            // the write survives a cancelled request. usage.Usage is populated here;
            // PR4 will write credit_ledger.
            await PersistScanResultAsync(convo, full.ToString(), usage.Usage);
        }
    }

    public async IAsyncEnumerable<string> AskStreamAsync(
        Guid userId, Guid conversationId, string question,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var convo = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (convo is null) throw new InvalidOperationException("Conversation not found");

        var history = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(ct);

        // Persist user message immediately so order is correct
        db.Messages.Add(new Message
        {
            ConversationId = convo.Id,
            Role = MessageRoles.User,
            Content = question,
        });
        await db.SaveChangesAsync(ct);

        var prompt = Prompts.ForChat(
            history.Select(h => (h.Role, h.Content)),
            question,
            convo.TargetLang);

        var usage = new UsageBox();
        var full = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in gemini.StreamAsync(prompt, usage, ct))
            {
                full.Append(chunk);
                yield return chunk;
            }
        }
        finally
        {
            // Persist the assistant reply even if the client disconnected mid-stream.
            await PersistAskResultAsync(convo, full.ToString(), usage.Usage);
        }
    }

    // Persist the streamed scan answer + extracted title. Called from a finally so a
    // mid-stream client disconnect doesn't lose what the user already saw. Title is
    // clamped to the column limit (varchar(200)) defensively, independent of
    // TitleExtractor's own cap, so the two can't silently drift apart.
    private async Task PersistScanResultAsync(
        Conversation convo, string fullText, AiTokenUsage usage)
    {
        if (fullText.Length == 0) return;          // nothing streamed (e.g. instant disconnect)

        var (title, body) = TitleExtractor.Extract(fullText);
        if (!string.IsNullOrWhiteSpace(title))
            convo.Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;

        db.Messages.Add(new Message
        {
            ConversationId = convo.Id,
            Role = MessageRoles.Assistant,
            Content = body.Length > 0 ? body : fullText,
        });
        quota.AddUsageEvent(convo.UserId, convo.Id, UsageKinds.Scan, usage, ocrCalled: true);
        convo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task PersistAskResultAsync(
        Conversation convo, string fullText, AiTokenUsage usage)
    {
        if (fullText.Length == 0) return;

        db.Messages.Add(new Message
        {
            ConversationId = convo.Id,
            Role = MessageRoles.Assistant,
            Content = fullText,
        });
        quota.AddUsageEvent(convo.UserId, convo.Id, UsageKinds.Ask, usage, ocrCalled: false);
        convo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string StorageKey(Guid userId, Guid convId) =>
        $"users/{userId:N}/{convId:N}.jpg";

    private static ConversationDto ToDto(Conversation c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        Topic = c.Topic,
        RootLang = c.RootLang,
        TargetLang = c.TargetLang,
        HasImage = c.ImageStorageKey is not null,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static ConversationDetailDto ToDetailDto(Conversation c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        Topic = c.Topic,
        RootLang = c.RootLang,
        TargetLang = c.TargetLang,
        HasImage = c.ImageStorageKey is not null,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };
}
