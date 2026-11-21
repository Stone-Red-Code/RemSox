namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// What the owner of a <see cref="TextLogin"/> decides about the logins tried at it, as
/// <c>IRpcLogins</c> does for remote desktop: whether one may be tried at all, and what to make
/// of its outcome. The owner knows where its clients come from; the login prompt does not.
/// Telnet uses one to limit guessing across connections (<see cref="Accounts.LoginThrottle"/>);
/// the console uses none.
/// </summary>
public interface ILoginGate
{
    /// <summary>
    /// Returns why a login as <paramref name="name"/> may not be tried now (shown to the user), or
    /// null when it may. Asked before the password is checked, so a refused try costs nothing.
    /// </summary>
    string? Refuse(string name);

    /// <summary>Tells the owner whether a login as <paramref name="name"/> gave the right password.</summary>
    void Report(string name, bool rightPassword);
}
