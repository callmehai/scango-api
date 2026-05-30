using FluentAssertions;
using ScanGo.Api.Features.Ai;

namespace ScanGo.Api.Tests.Features.Ai;

public class TitleExtractorTests
{
    [Fact]
    public void Extract_HappyPath()
    {
        var input = "TITLE: Dầu gội Head & Shoulders\n\nThành phần chính...";
        var (title, body) = TitleExtractor.Extract(input);
        title.Should().Be("Dầu gội Head & Shoulders");
        body.Should().Be("Thành phần chính...");
    }

    [Fact]
    public void Extract_HandlesNoTitle()
    {
        var input = "Just a regular response\nwith no title prefix";
        var (title, body) = TitleExtractor.Extract(input);
        title.Should().BeEmpty();
        body.Should().Be(input);
    }

    [Fact]
    public void Extract_HandlesSingleLine()
    {
        var (title, body) = TitleExtractor.Extract("single line no newline");
        title.Should().BeEmpty();
        body.Should().Be("single line no newline");
    }

    [Fact]
    public void Extract_TruncatesLongTitle()
    {
        var longTitle = new string('x', 300);
        var input = $"TITLE: {longTitle}\nbody";
        var (title, _) = TitleExtractor.Extract(input);
        title.Length.Should().Be(200);
    }

    [Fact]
    public void Extract_CaseInsensitivePrefix()
    {
        var (title, _) = TitleExtractor.Extract("title: lowercase\nbody");
        title.Should().Be("lowercase");
    }
}
