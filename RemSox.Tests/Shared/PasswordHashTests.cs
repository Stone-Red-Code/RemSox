using RemSox.Shared.Cryptography;

namespace RemSox.Tests.Shared;

public sealed class PasswordHashTests
{
    // Few rounds: these tests check the format and the comparison, not the cost.
    private const int Iterations = 1000;

    [Fact]
    public void MatchesOnlyItsOwnPassword()
    {
        PasswordHash hash = PasswordHash.Create("secret", Iterations);

        Assert.True(hash.Matches("secret"));
        Assert.False(hash.Matches("Secret"));
        Assert.False(hash.Matches(string.Empty));
    }

    [Fact]
    public void SamePasswordGetsDifferentSalts()
    {
        Assert.NotEqual(PasswordHash.Create("secret", Iterations).Salt, PasswordHash.Create("secret", Iterations).Salt);
    }

    [Fact]
    public void SurvivesBeingWrittenAndRead()
    {
        PasswordHash hash = PasswordHash.Create("secret", Iterations);

        PasswordHash? read = PasswordHash.Parse(hash.ToString());

        Assert.NotNull(read);
        Assert.Equal(Iterations, read.Iterations);
        Assert.True(read.Matches("secret"));
        Assert.StartsWith("pbkdf2-sha256$1000$", hash.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret")]
    [InlineData("md5$1000$00$00")]
    [InlineData("pbkdf2-sha256$0$00000000000000000000000000000000$0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("pbkdf2-sha256$-5$00000000000000000000000000000000$0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("pbkdf2-sha256$1000$0000$0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("pbkdf2-sha256$1000$zz000000000000000000000000000000$0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("pbkdf2-sha256$1000$00000000000000000000000000000000$00")]
    public void ParseRefusesWhatIsNotAHash(string text)
    {
        Assert.Null(PasswordHash.Parse(text));
    }
}
