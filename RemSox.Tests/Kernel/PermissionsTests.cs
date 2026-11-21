using RemSox.Kernel.Accounts;
using RemSox.Kernel.Security;

using static RemSox.Tests.Kernel.KernelState;

namespace RemSox.Tests.Kernel;

/// <summary>
/// Who may read and write what: the built-in rules that follow the disk layout, and the rules
/// file adding to them. Paths are given canonical, as <see cref="Permissions.Canonicalize"/>
/// would return them (that needs a FAT disk, so it is not tested here).
/// </summary>
[Collection(KernelState.Name)]
public sealed class PermissionsTests
{
    private const int DevGroupId = 0;

    private static readonly Account Bob = new(0, "bob", []);
    private static readonly Account Alice = new(1, "alice", [Group.AdminsId]);
    private static readonly Account Carol = new(2, "carol", [DevGroupId]);

    public PermissionsTests()
    {
        PermissionRules.Load([]);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/system/config/settings.conf")]
    [InlineData("/system/startmenu")]
    [InlineData("/programs/tool.rsh")]
    [InlineData("/users")]
    public void EveryoneReadsTheSystemButCannotChangeIt(string path)
    {
        Assert.Equal(Access.Read, Permissions.GetAccess(As(Bob), path));
    }

    [Fact]
    public void AdministratorsChangeTheSystemOnlyElevated()
    {
        Assert.Equal(Access.Read, Permissions.GetAccess(As(Alice), "/system/config/settings.conf"));
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Alice, elevated: true), "/system/config/settings.conf"));
    }

    [Fact]
    public void ElevatingDoesNothingForSomeoneWhoIsNoAdministrator()
    {
        Assert.Equal(Access.Read, Permissions.GetAccess(As(Bob, elevated: true), "/system/config/settings.conf"));
    }

    [Fact]
    public void OnlyTheSystemReachesTheSecrets()
    {
        Assert.Equal(Access.None, Permissions.GetAccess(As(Bob), "/system/secrets/passwords"));
        Assert.Equal(Access.None, Permissions.GetAccess(As(Alice, elevated: true), "/system/secrets/passwords"));
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(Caller.System, "/system/secrets/passwords"));
    }

    [Fact]
    public void LogsAreTheAdministrators()
    {
        Assert.Equal(Access.None, Permissions.GetAccess(As(Bob), "/system/logs/remsox.log"));
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Alice, elevated: true), "/system/logs/remsox.log"));
    }

    [Fact]
    public void EveryoneUsesTemp()
    {
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Bob), "/temp/scratch.txt"));
    }

    [Theory]
    [InlineData("/users/bob")]
    [InlineData("/users/bob/home/notes.txt")]
    [InlineData("/users/bob/config/startup.rsh")]
    [InlineData("/users/BOB/home")]
    public void ProfileIsItsOwners(string path)
    {
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Bob), path));
        Assert.Equal(Access.None, Permissions.GetAccess(As(Carol), path));
        Assert.Equal(Access.None, Permissions.GetAccess(As(Alice), path));
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Alice, elevated: true), path));
    }

    [Theory]
    [InlineData("/users/bob.deleted/home/notes.txt")]
    [InlineData("/users/bobby/home")]
    public void ProfileWithAnotherNameIsNotOwned(string path)
    {
        // A deleted account's profile, moved aside, and a name that only starts the same.
        Assert.Equal(Access.None, Permissions.GetAccess(As(Bob), path));
    }

    [Fact]
    public void FileRuleGrantsAGroupAFolder()
    {
        PermissionRules.Load(["/shared/dev group:0 read,write"]);

        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Carol), "/shared/dev/plan.txt"));
        Assert.Equal(Access.Read, Permissions.GetAccess(As(Bob), "/shared/dev/plan.txt"));
    }

    [Fact]
    public void MostSpecificPathWithRulesDecides()
    {
        PermissionRules.Load([
            "/shared everyone read,write",
            "/shared/dev group:0 read,write",
        ]);

        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Bob), "/shared/other/file.txt"));

        // /shared/dev has rules of its own, which do not name bob: the ones on /shared do not count there.
        Assert.Equal(Access.Read, Permissions.GetAccess(As(Bob), "/shared/dev/plan.txt"));
        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Carol), "/shared/dev/plan.txt"));
    }

    [Fact]
    public void FileRulesNeverReachTheSecrets()
    {
        PermissionRules.Load([
            "/ everyone read,write",
            "/system/secrets everyone read",
        ]);

        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Bob), "/system/config/settings.conf"));
        Assert.Equal(Access.None, Permissions.GetAccess(As(Bob), "/system/secrets"));
        Assert.Equal(Access.None, Permissions.GetAccess(As(Bob), "/system/secrets/passwords"));
    }

    [Fact]
    public void FileRulesOnlyAdd()
    {
        PermissionRules.Load(["/users/bob user:2 read"]);

        Assert.Equal(Access.ReadWrite, Permissions.GetAccess(As(Bob), "/users/bob/home/notes.txt"));
        Assert.Equal(Access.Read, Permissions.GetAccess(As(Carol), "/users/bob/home/notes.txt"));
    }

    [Fact]
    public void AdminsGroupInARuleCountsOnlyElevated()
    {
        PermissionRules.Load(["/users/bob group:-1 read"]);

        Assert.Equal(Access.None, Permissions.GetAccess(As(Alice), "/users/bob/home"));
    }

    [Theory]
    [InlineData("/users/bob/home", "bob")]
    [InlineData("/users/bob", "bob")]
    [InlineData("/users", null)]
    [InlineData("/usersx/bob", null)]
    [InlineData("/temp", null)]
    public void ProfileOwnerComesFromTheFolderName(string path, string? owner)
    {
        Assert.Equal(owner, Permissions.GetProfileOwner(path));
    }

    [Theory]
    [InlineData("/a/b", "/a", true)]
    [InlineData("/a", "/a", true)]
    [InlineData("/A/b", "/a/", true)]
    [InlineData("/ab", "/a", false)]
    [InlineData("/a", "/a/b", false)]
    [InlineData("/anything", "/", true)]
    public void IsUnderMatchesWholeFolderNames(string path, string folder, bool expected)
    {
        Assert.Equal(expected, Permissions.IsUnder(path, folder));
    }
}
