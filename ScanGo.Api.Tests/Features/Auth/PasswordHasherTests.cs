using FluentAssertions;
using ScanGo.Api.Features.Auth;

namespace ScanGo.Api.Tests.Features.Auth;

public class PasswordHasherTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short1")]      // 6 chars
    [InlineData("abc1234")]     // 7 chars
    public void Validate_TooShort(string pw)
    {
        PasswordHasher.Validate(pw).Should().Be(PasswordValidationResult.TooShort);
    }

    [Theory]
    [InlineData("abcdefgh")]    // no digit
    [InlineData("12345678")]    // no letter
    [InlineData("!@#$%^&*")]    // neither
    public void Validate_NeedsLetterAndDigit(string pw)
    {
        PasswordHasher.Validate(pw).Should().Be(PasswordValidationResult.NeedsLetterAndDigit);
    }

    [Theory]
    [InlineData("abc12345")]
    [InlineData("Hello123")]
    [InlineData("p@ssword1")]
    public void Validate_Ok(string pw)
    {
        PasswordHasher.Validate(pw).Should().Be(PasswordValidationResult.Ok);
    }

    [Fact]
    public void Hash_ProducesDifferentValuesForSameInput()
    {
        var a = PasswordHasher.Hash("hello123");
        var b = PasswordHasher.Hash("hello123");
        a.Should().NotBe(b, "bcrypt embeds a random salt");
    }

    [Fact]
    public void Verify_AcceptsCorrectPassword()
    {
        var hash = PasswordHasher.Hash("hello123");
        PasswordHasher.Verify("hello123", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = PasswordHasher.Hash("hello123");
        PasswordHasher.Verify("hello124", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_HandlesMalformedHashGracefully()
    {
        PasswordHasher.Verify("anything", "not-a-real-bcrypt-hash").Should().BeFalse();
    }
}
