using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Conversations;
using ScanGo.Api.Tests.Features.Auth;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanGo.Api.Tests.Features.Conversations;

public class ConversationsFlowTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    private async Task<HttpClient> AuthedAsync(string email)
    {
        var http = fx.CreateClient();
        var reg = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "Password1",
            Name = "T",
            TermsAccepted = true,
            PrivacyAccepted = true,
        });
        reg.EnsureSuccessStatusCode();
        var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return http;
    }

    private static MultipartFormDataContent BuildScanForm(
        byte[] imageBytes,
        string topic = "general",
        string root = "auto",
        string target = "vnm",
        string filename = "test.jpg")
    {
        var form = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(imageBytes);
        imgContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(imgContent, "image", filename);
        form.Add(new StringContent(topic), "topic");
        form.Add(new StringContent(root), "rootLang");
        form.Add(new StringContent(target), "targetLang");
        return form;
    }

    private static byte[] MakeJpeg(int width = 100, int height = 100)
    {
        using var img = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task ScanCreate_RequiresAuth()
    {
        var http = fx.CreateClient();
        var resp = await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg()));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ScanCreate_HappyPath_StoresImage_ReturnsId()
    {
        var http = await AuthedAsync("conv1@example.com");
        var resp = await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg()));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<ConversationDto>();
        dto!.Id.Should().NotBe(Guid.Empty);
        dto.Topic.Should().Be("general");
        dto.RootLang.Should().Be("auto");
        dto.TargetLang.Should().Be("vnm");
        dto.HasImage.Should().BeTrue();
    }

    [Fact]
    public async Task ScanCreate_RejectsInvalidTopic()
    {
        var http = await AuthedAsync("conv2@example.com");
        var resp = await http.PostAsync(
            "/api/conversations/scan-create",
            BuildScanForm(MakeJpeg(), topic: "fake-topic"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ScanCreate_RejectsTooLargeImage()
    {
        var http = await AuthedAsync("conv3@example.com");
        var huge = new byte[12 * 1024 * 1024];     // 12 MB > 10 MB cap
        new Random(0).NextBytes(huge);
        var resp = await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(huge));
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "12 MB exceeds RequestSizeLimit 15 MB only after ConversationService rejects 10 MB");
    }

    [Fact]
    public async Task History_ListsOnlyOwnConversations()
    {
        var alice = await AuthedAsync("alice-conv@example.com");
        var bob = await AuthedAsync("bob-conv@example.com");

        (await alice.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .EnsureSuccessStatusCode();
        (await alice.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg(), topic: "product")))
            .EnsureSuccessStatusCode();
        (await bob.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .EnsureSuccessStatusCode();

        var page = await (await alice.GetAsync("/api/conversations/history"))
            .Content.ReadFromJsonAsync<HistoryPageDto>();
        page!.Total.Should().Be(2);

        var bobPage = await (await bob.GetAsync("/api/conversations/history"))
            .Content.ReadFromJsonAsync<HistoryPageDto>();
        bobPage!.Total.Should().Be(1);
    }

    [Fact]
    public async Task History_Pagination_RespectsSkipLimit()
    {
        var http = await AuthedAsync("page@example.com");
        for (var i = 0; i < 5; i++)
        {
            (await http.PostAsync(
                "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
                .EnsureSuccessStatusCode();
        }

        var page1 = await (await http.GetAsync("/api/conversations/history?skip=0&limit=2"))
            .Content.ReadFromJsonAsync<HistoryPageDto>();
        page1!.Items.Should().HaveCount(2);
        page1.Total.Should().Be(5);
        page1.HasMore.Should().BeTrue();

        var page3 = await (await http.GetAsync("/api/conversations/history?skip=4&limit=2"))
            .Content.ReadFromJsonAsync<HistoryPageDto>();
        page3!.Items.Should().HaveCount(1);
        page3.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task History_FilterByTopic()
    {
        var http = await AuthedAsync("topic-filter@example.com");
        (await http.PostAsync("/api/conversations/scan-create",
            BuildScanForm(MakeJpeg(), topic: "product"))).EnsureSuccessStatusCode();
        (await http.PostAsync("/api/conversations/scan-create",
            BuildScanForm(MakeJpeg(), topic: "history"))).EnsureSuccessStatusCode();
        (await http.PostAsync("/api/conversations/scan-create",
            BuildScanForm(MakeJpeg(), topic: "history"))).EnsureSuccessStatusCode();

        var page = await (await http.GetAsync("/api/conversations/history?topic=history"))
            .Content.ReadFromJsonAsync<HistoryPageDto>();
        page!.Total.Should().Be(2);
        page.Items.Should().AllSatisfy(c => c.Topic.Should().Be("history"));
    }

    [Fact]
    public async Task GetOne_ReturnsMessages_AndDetectsOtherUserAs404()
    {
        var alice = await AuthedAsync("alice-get@example.com");
        var bob = await AuthedAsync("bob-get@example.com");

        var created = await (await alice.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        var alicesView = await (await alice.GetAsync($"/api/conversations/{created!.Id}"))
            .Content.ReadFromJsonAsync<ConversationDetailDto>();
        alicesView!.Id.Should().Be(created.Id);
        alicesView.Messages.Should().BeEmpty();

        // Bob trying to read alice's conversation -> 404 (not 403 — leak less)
        var bobView = await bob.GetAsync($"/api/conversations/{created.Id}");
        bobView.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Image_ReturnsBinary()
    {
        var http = await AuthedAsync("img@example.com");
        var created = await (await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        var resp = await http.GetAsync($"/api/conversations/{created!.Id}/image");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Rename_UpdatesTitle()
    {
        var http = await AuthedAsync("rename@example.com");
        var created = await (await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        (await http.PatchAsJsonAsync($"/api/conversations/{created!.Id}",
            new RenameConversationRequest { Title = "  My new name  " }))
            .EnsureSuccessStatusCode();

        var view = await (await http.GetAsync($"/api/conversations/{created.Id}"))
            .Content.ReadFromJsonAsync<ConversationDetailDto>();
        view!.Title.Should().Be("My new name");
    }

    [Fact]
    public async Task Rename_RejectsEmptyOrTooLong()
    {
        var http = await AuthedAsync("rename2@example.com");
        var created = await (await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        var empty = await http.PatchAsJsonAsync($"/api/conversations/{created!.Id}",
            new RenameConversationRequest { Title = "   " });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var tooLong = await http.PatchAsJsonAsync($"/api/conversations/{created.Id}",
            new RenameConversationRequest { Title = new string('x', 250) });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_RemovesConversationAndImage()
    {
        var http = await AuthedAsync("del-conv@example.com");
        var created = await (await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();

        (await http.DeleteAsync($"/api/conversations/{created!.Id}"))
            .EnsureSuccessStatusCode();

        var after = await http.GetAsync($"/api/conversations/{created.Id}");
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var img = await http.GetAsync($"/api/conversations/{created.Id}/image");
        img.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SearchByTitle_MatchesCaseInsensitive()
    {
        var http = await AuthedAsync("search@example.com");
        var c1 = await (await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();
        (await http.PatchAsJsonAsync($"/api/conversations/{c1!.Id}",
            new RenameConversationRequest { Title = "Dầu gội Head & Shoulders" }))
            .EnsureSuccessStatusCode();

        var c2 = await (await http.PostAsync(
            "/api/conversations/scan-create", BuildScanForm(MakeJpeg())))
            .Content.ReadFromJsonAsync<ConversationDto>();
        (await http.PatchAsJsonAsync($"/api/conversations/{c2!.Id}",
            new RenameConversationRequest { Title = "Kem chống nắng" }))
            .EnsureSuccessStatusCode();

        var page = await (await http.GetAsync("/api/conversations/history?q=head"))
            .Content.ReadFromJsonAsync<HistoryPageDto>();
        page!.Total.Should().Be(1);
        page.Items[0].Id.Should().Be(c1.Id);
    }
}
