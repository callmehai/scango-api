using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Conversations;
using ScanGo.Api.Tests.Features.Auth;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanGo.Api.Tests.Features.Conversations;

public class SseStreamTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    private async Task<HttpClient> AuthedAsync(string email)
    {
        var http = fx.CreateClient();
        var reg = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email, Password = "Password1", Name = "T",
            TermsAccepted = true, PrivacyAccepted = true,
        });
        reg.EnsureSuccessStatusCode();
        var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return http;
    }

    private static byte[] MakeJpeg()
    {
        using var img = new Image<Rgba32>(100, 100);
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    private static MultipartFormDataContent ScanForm(byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(bytes);
        imgContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(imgContent, "image", "test.jpg");
        form.Add(new StringContent("general"), "topic");
        form.Add(new StringContent("auto"), "rootLang");
        form.Add(new StringContent("vnm"), "targetLang");
        return form;
    }

    /// <summary>Parse `data: "<json>"` chunks back to plain text deltas.</summary>
    private static List<string> ParseSse(string raw)
    {
        var deltas = new List<string>();
        foreach (var line in raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("data: ")) continue;
            var payload = trimmed[6..];
            if (payload == "[DONE]") continue;
            deltas.Add(JsonSerializer.Deserialize<string>(payload)!);
        }
        return deltas;
    }

    [Fact]
    public async Task ScanStream_EmitsChunks_EndsWithDone_PersistsAssistantMessage()
    {
        var http = await AuthedAsync("scan-stream@example.com");
        var created = await (await http.PostAsync(
            "/api/conversations/scan-create", ScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        var resp = await http.PostAsync(
            $"/api/conversations/{created!.Id}/scan-stream", content: null);
        resp.EnsureSuccessStatusCode();
        resp.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var raw = await resp.Content.ReadAsStringAsync();
        raw.Should().Contain("data: ").And.EndWith("[DONE]\n\n");

        var deltas = ParseSse(raw);
        deltas.Should().NotBeEmpty();
        string.Concat(deltas).Should().Contain("Head & Shoulders");

        // Verify persisted state
        await using var db = fx.CreateDbContext();
        var convo = await db.Conversations.FirstAsync(c => c.Id == created.Id);
        convo.Title.Should().Be("Dầu gội Head & Shoulders trị gàu");

        var assistant = await db.Messages
            .Where(m => m.ConversationId == created.Id && m.Role == "assistant")
            .FirstOrDefaultAsync();
        assistant.Should().NotBeNull();
        assistant!.Content.Should().NotContain("TITLE:", "title stripped from body");
    }

    [Fact]
    public async Task AskStream_PersistsUserAndAssistantMessages()
    {
        var http = await AuthedAsync("ask-stream@example.com");
        var created = await (await http.PostAsync(
            "/api/conversations/scan-create", ScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        // First run scan so there's prior context
        (await http.PostAsync($"/api/conversations/{created!.Id}/scan-stream", null))
            .EnsureSuccessStatusCode();

        // Now ask follow-up
        var resp = await http.PostAsJsonAsync(
            $"/api/conversations/{created.Id}/ask-stream",
            new AskRequest { Question = "Sản phẩm này có an toàn không?" });
        resp.EnsureSuccessStatusCode();

        var raw = await resp.Content.ReadAsStringAsync();
        raw.Should().EndWith("[DONE]\n\n");

        await using var db = fx.CreateDbContext();
        var msgs = await db.Messages
            .Where(m => m.ConversationId == created.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        msgs.Should().HaveCount(3, "scan-assistant + user-question + ask-assistant");
        msgs[0].Role.Should().Be("assistant");      // from scan-stream
        msgs[1].Role.Should().Be("user");           // user question
        msgs[1].Content.Should().Be("Sản phẩm này có an toàn không?");
        msgs[2].Role.Should().Be("assistant");      // ask-stream reply
    }
}
