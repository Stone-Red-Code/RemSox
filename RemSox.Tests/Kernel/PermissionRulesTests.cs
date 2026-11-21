using RemSox.Kernel.Accounts;
using RemSox.Kernel.Security;

namespace RemSox.Tests.Kernel;

/// <summary>Reading the rules file and the words it is written in.</summary>
[Collection(KernelState.Name)]
public sealed class PermissionRulesTests
{
    [Fact]
    public void ReadsRulesAndSkipsWhatIsNotOne()
    {
        PermissionRules.Load([
            "# a comment",
            string.Empty,
            "/shared everyone read",
            "  \"/shared/my files\" group:0 read,write  ",
            "/users/bob user:2 write",
            "relative/path everyone read",
            "/shared owner read",
            "/shared everyone execute",
            "/shared everyone",
            "/shared everyone read extra",
        ]);

        Assert.Equal(
            [
                new PermissionRule("/shared", RuleSubject.Everyone, Access.Read),
                new PermissionRule("/shared/my files", new RuleSubject(RuleSubjectKind.Group, 0), Access.ReadWrite),
                new PermissionRule("/users/bob", new RuleSubject(RuleSubjectKind.Account, 2), Access.Write),
            ],
            PermissionRules.All);
    }

    [Fact]
    public void LoadingReplacesTheRules()
    {
        PermissionRules.Load(["/a everyone read"]);
        PermissionRules.Load(["/b everyone read"]);

        Assert.Equal("/b", Assert.Single(PermissionRules.All).Path);
    }

    [Theory]
    [InlineData("read", Access.Read)]
    [InlineData("write", Access.Write)]
    [InlineData("read,write", Access.ReadWrite)]
    [InlineData("write, read", Access.ReadWrite)]
    [InlineData("READ", Access.Read)]
    public void ParsesRights(string text, Access rights)
    {
        Assert.Equal(rights, PermissionRules.ParseRights(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("execute")]
    [InlineData("read,execute")]
    public void RefusesWhatAreNoRights(string text)
    {
        Assert.Null(PermissionRules.ParseRights(text));
    }

    [Theory]
    [InlineData(Access.Read)]
    [InlineData(Access.Write)]
    [InlineData(Access.ReadWrite)]
    public void RightsSurviveBeingWrittenAndRead(Access rights)
    {
        Assert.Equal(rights, PermissionRules.ParseRights(PermissionRules.FormatRights(rights)));
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("user:3")]
    [InlineData("group:0")]
    [InlineData("group:-1")]
    public void SubjectSurvivesBeingWrittenAndRead(string text)
    {
        Assert.Equal(text, RuleSubject.Parse(text)?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("user:bob")]
    [InlineData("user:")]
    [InlineData("role:1")]
    [InlineData("user:1:2")]
    public void RefusesWhatIsNoSubject(string text)
    {
        // "owner" is only for built-in rules: a file rule cannot hand out profiles.
        Assert.Null(RuleSubject.Parse(text));
    }

    [Fact]
    public void MostSpecificPathDecidesAmongTheGivenRules()
    {
        PermissionRule[] rules =
        [
            new("/shared", RuleSubject.Everyone, Access.Read),
            new("/shared/dev", RuleSubject.Everyone, Access.Write),
            new("/shared/dev", new RuleSubject(RuleSubjectKind.Group, 0), Access.ReadWrite),
        ];

        Assert.Equal(rules[1..], PermissionRules.GetDecidingRules(rules, "/shared/dev/x"));
        Assert.Equal(rules[..1], PermissionRules.GetDecidingRules(rules, "/shared/developers"));
        Assert.Empty(PermissionRules.GetDecidingRules(rules, "/elsewhere"));
    }
}
