using RemSox.Kernel.Configuration;

namespace RemSox.Tests.Kernel;

/// <summary>The <c>key = value</c> files (settings, shortcuts), changed line by line.</summary>
public sealed class KeyValueFileTests
{
    [Fact]
    public void ReadsValuesAndIgnoresTheRest()
    {
        KeyValueFile file = new([
            "# comment = not a setting",
            string.Empty,
            "no equals sign",
            "= no key",
            "  Login.Screen  =  graphical  ",
            "empty =",
        ]);

        Assert.Equal([("login.screen", "graphical"), ("empty", string.Empty)], file.Entries);
        Assert.Equal("graphical", file.Get("login.screen"));
        Assert.Null(file.Get("comment"));
    }

    [Fact]
    public void LastLineForAKeyWins()
    {
        KeyValueFile file = new(["a = 1", "a = 2"]);

        Assert.Equal("2", file.Get("a"));
    }

    [Fact]
    public void SetChangesTheFirstLineAndDropsTheLaterOnes()
    {
        KeyValueFile file = new(["# header", "a = 1", "b = x", "A = 2", "# footer"]);

        file.Set("a", "3");

        Assert.Equal(["# header", "a = 3", "b = x", "# footer"], file.Lines);
    }

    [Fact]
    public void SetAddsANewKeyAtTheEnd()
    {
        KeyValueFile file = new(["# header"]);

        file.Set("a", "1");
        file.Set("b", string.Empty);

        Assert.Equal(["# header", "a = 1", "b ="], file.Lines);
    }

    [Fact]
    public void RemoveDropsEveryLineOfTheKey()
    {
        KeyValueFile file = new(["a = 1", "b = 2", "a = 3"]);

        Assert.True(file.Remove("a"));
        Assert.False(file.Remove("a"));
        Assert.Equal(["b = 2"], file.Lines);
    }

    [Fact]
    public void CopyIsIndependent()
    {
        KeyValueFile file = new(["a = 1"]);
        KeyValueFile copy = file.Copy();

        file.Set("a", "2");

        Assert.Equal("1", copy.Get("a"));
    }
}
