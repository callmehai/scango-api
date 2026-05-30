using FluentAssertions;
using ScanGo.Api.Common;

namespace ScanGo.Api.Tests.Common;

public class TokenHasherTests
{
    [Fact]
    public void Hash_IsDeterministic()
    {
        TokenHasher.Hash("abc").Should().Be(TokenHasher.Hash("abc"));
    }

    [Fact]
    public void Hash_DifferentInputsProduceDifferentHashes()
    {
        TokenHasher.Hash("abc").Should().NotBe(TokenHasher.Hash("abd"));
    }

    [Fact]
    public void Hash_OutputIs64HexChars()
    {
        var h = TokenHasher.Hash("anything");
        h.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void GenerateRandom_ProducesUniqueTokens()
    {
        var a = TokenHasher.GenerateRandom();
        var b = TokenHasher.GenerateRandom();
        a.Should().NotBe(b);
        a.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }
}
