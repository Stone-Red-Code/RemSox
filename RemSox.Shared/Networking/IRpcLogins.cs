using RemSox.Shared.Cryptography;

namespace RemSox.Shared.Networking;

/// <summary>
/// What a <see cref="TcpRpcServer"/> asks its owner while a client logs in: the accounts and
/// their stored password hashes, and whether a login may be tried.
/// </summary>
public interface IRpcLogins
{
    /// <summary>Returns the stored password hash of <paramref name="user"/>, or null when there is no such account (or it has no password).</summary>
    PasswordHash? FindPassword(string user);

    /// <summary>
    /// Returns why a login as <paramref name="user"/> may not even be tried now (too many failed
    /// ones; shown to the client), or null when it may. Asked before the password is checked.
    /// </summary>
    string? Throttle(string user);

    /// <summary>Tells the owner whether a login as <paramref name="user"/> gave the right password, so it can count failures.</summary>
    void Report(string user, bool rightPassword);
}
