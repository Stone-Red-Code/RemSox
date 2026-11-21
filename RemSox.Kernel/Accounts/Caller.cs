namespace RemSox.Kernel.Accounts;

/// <summary>
/// Who is asking for something: a logon session, and whether this request is elevated (run with
/// <c>sudo</c>, or by a program started that way). Every permission check is made for a caller.
/// </summary>
/// <param name="Session">The logon session the request comes from.</param>
/// <param name="Elevated">Whether an administrator asked for admin rights for this request.</param>
public readonly record struct Caller(LogonSession Session, bool Elevated)
{
    /// <summary>The system itself: the boot, services and the kernel's own work. It may do everything.</summary>
    public static Caller System => new(LogonSessions.System, Elevated: false);

    public Account Account => Session.Account;

    public bool IsSystem => Session.IsSystem;

    /// <summary>
    /// Gets whether admin-only things are allowed: for the system, and for an administrator who
    /// elevated. An administrator who did not works with ordinary rights.
    /// </summary>
    public bool HasAdminRights => IsSystem || (Elevated && Account.IsAdmin);

    /// <summary>
    /// Returns why the caller may not do something only administrators may (to show the user), or
    /// null when they may: an administrator is told to use <c>sudo</c>.
    /// </summary>
    public string? DenyUnlessAdmin(string what)
    {
        if (HasAdminRights)
        {
            return null;
        }

        return Account.IsAdmin
            ? $"To {what} you need administrator rights: run it with sudo."
            : $"Only an administrator can {what}.";
    }
}
