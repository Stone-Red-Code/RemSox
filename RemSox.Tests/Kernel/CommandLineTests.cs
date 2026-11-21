using RemSox.Kernel.UI.CLI;

namespace RemSox.Tests.Kernel;

/// <summary>How the shell splits a line, which the permission rules file also uses for its paths.</summary>
public sealed class CommandLineTests
{
    [Theory]
    [InlineData("ls", new[] { "ls" })]
    [InlineData("  cp  a   b  ", new[] { "cp", "a", "b" })]
    [InlineData("cat \"my file.txt\"", new[] { "cat", "my file.txt" })]
    [InlineData("echo a\"b c\"d", new[] { "echo", "ab cd" })]
    [InlineData("echo \\\"quoted\\\"", new[] { "echo", "\"quoted\"" })]
    [InlineData("echo back\\\\slash", new[] { "echo", "back\\slash" })]
    [InlineData("echo \"\"", new[] { "echo", "" })]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    public void SplitsIntoWords(string line, string[] words)
    {
        Assert.Equal(words, CommandLine.SplitWords(line));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("tab\there")]
    [InlineData("\"quoted\"")]
    [InlineData("back\\slash")]
    [InlineData("ends with \\")]
    [InlineData("/users/bob/my files")]
    public void QuotedWordComesBackUnchanged(string word)
    {
        Assert.Equal([word], CommandLine.SplitWords(CommandLine.Quote(word)));
    }

    [Fact]
    public void ParseKeepsTheRawArguments()
    {
        CommandLine? line = CommandLine.Parse("  echo  \"a  b\"  c  ");

        Assert.NotNull(line);
        Assert.Equal("echo", line.Name);
        Assert.Equal(["a  b", "c"], line.Arguments);
        Assert.Equal("\"a  b\"  c", line.RawArguments);
    }

    [Fact]
    public void BlankLineIsNoCommand()
    {
        Assert.Null(CommandLine.Parse("   "));
    }
}
