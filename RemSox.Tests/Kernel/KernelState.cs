using RemSox.Kernel.Accounts;

namespace RemSox.Tests.Kernel;

/// <summary>
/// Tests of the kernel's static state (the permission rules, the login throttle) share it, so
/// they run one at a time in this collection rather than in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KernelState
{
    public const string Name = "Kernel state";

    /// <summary>Returns a caller logged in as <paramref name="account"/>, elevated with <c>admin</c> or not.</summary>
    public static Caller As(Account account, bool elevated = false)
    {
        return new Caller(new LogonSession(1, account, LogonKind.Console, "test"), elevated);
    }
}
