using RemSox.Kernel.Accounts;

namespace RemSox.Tests.Kernel;

/// <summary>Which session is at the machine, as logins there and remote desktop move it.</summary>
[Collection(KernelState.Name)]
public sealed class LogonSessionsTests
{
    private static readonly Account Alice = new(100, "alice", []);
    private static readonly Account Bob = new(101, "bob", []);

    [Fact]
    public void ConsoleLoginTakesTheMachineAndOthersDoNot()
    {
        LogonSession console = LogonSessions.Start(Alice, LogonKind.Console, "test");
        LogonSession telnet = LogonSessions.Start(Bob, LogonKind.Telnet, "test");
        LogonSession remote = LogonSessions.Start(Bob, LogonKind.RemoteDesktop, "test");
        try
        {
            Assert.Same(console, LogonSessions.AtMachine);
            Assert.True(console.IsAtMachine);
            Assert.False(telnet.IsAtMachine);
            Assert.False(remote.IsAtMachine);
        }
        finally
        {
            End(console, telnet, remote);
        }
    }

    [Fact]
    public void SessionMovesAwayAndBackAndEndingItFreesTheMachine()
    {
        LogonSession session = LogonSessions.Start(Alice, LogonKind.Console, "test");
        int changes = 0;
        void Count() => changes++;
        LogonSessions.MachineChanged += Count;
        try
        {
            // A remote desktop viewer takes it: nobody is at the machine.
            LogonSessions.MoveToMachine(null);
            Assert.Null(LogonSessions.AtMachine);
            Assert.False(session.IsAtMachine);

            // Logging in at the machine brings it back.
            LogonSessions.MoveToMachine(session);
            LogonSessions.MoveToMachine(session);
            Assert.True(session.IsAtMachine);

            LogonSessions.End(session);
            Assert.Null(LogonSessions.AtMachine);
            Assert.Equal(3, changes);

            // An ended session cannot come back.
            LogonSessions.MoveToMachine(session);
            Assert.Null(LogonSessions.AtMachine);
        }
        finally
        {
            LogonSessions.MachineChanged -= Count;
            End(session);
        }
    }

    [Fact]
    public void RemoteDesktopSessionCanMoveToTheMachine()
    {
        LogonSession remote = LogonSessions.Start(Alice, LogonKind.RemoteDesktop, "test");
        try
        {
            Assert.Null(LogonSessions.AtMachine);

            LogonSessions.MoveToMachine(remote);
            Assert.True(remote.IsAtMachine);
        }
        finally
        {
            End(remote);
        }
    }

    private static void End(params LogonSession[] sessions)
    {
        foreach (LogonSession session in sessions)
        {
            LogonSessions.End(session);
        }
    }
}
