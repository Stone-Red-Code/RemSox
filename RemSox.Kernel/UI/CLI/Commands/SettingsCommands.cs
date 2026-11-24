using RemSox.Kernel.Accounts;
using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shows and changes the settings: by default your own (over the machine's, for the settings an
/// account may set itself), with <c>--system</c> the machine's, which only administrators change.
/// </summary>
public sealed class ConfigCommand : ICommand
{
    private const string SystemOption = "--system";

    public string Name => "config";

    public string Description => "Show or change your settings, or with --system the machine's";

    public string Usage => $"config list | get [{SystemOption}] <key> | set [{SystemOption}] <key> <value, \"\" for none> | reset [{SystemOption}] <key> | reload [{SystemOption}]";

    public int MaxArguments => 4;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        bool machine = context.Args.Any(arg => arg == SystemOption);
        string[] args = [.. context.Args.Where(arg => arg != SystemOption)];
        // Without an action it shows its usage, like user, group and session, rather than guessing one.
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        string key = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        Caller caller = context.Caller;

        int result = (action, args.Length, machine) switch
        {
            ("list", 1, false) => List(context),
            ("get", 2, _) => Get(context, key, machine),
            ("set", 3, true) => Report(context, SystemSettings.Set(caller, key, args[2]), $"{key} = {SystemSettings.Get(key)} (machine)"),
            ("set", 3, false) => Report(context, SystemSettings.SetOwn(caller, key, args[2]), $"{key} = {SystemSettings.Get(key, caller.Account)} (yours)"),
            ("reset", 2, true) => Report(context, SystemSettings.Reset(caller, key), SystemSettings.IsSet(key)
                ? $"{key} reset to {SystemSettings.Get(key)} (machine)"
                : $"{key} removed from the machine's settings (it has no default)"),
            ("reset", 2, false) => Report(context, SystemSettings.ResetOwn(caller, key), $"{key} is the machine's again: {Describe(key, caller.Account)}"),
            ("reload", 1, true) => ReloadMachine(context),
            ("reload", 1, false) => ReloadOwn(context),
            _ => context.UsageError(),
        };

        return Task.FromResult(result);
    }

    /// <summary>Shows every setting as it applies to you, and where each value comes from.</summary>
    private static int List(CommandContext context)
    {
        Account account = context.Caller.Account;

        foreach ((string key, string _, string description, SettingScope scope) in SystemSettings.Known)
        {
            context.Console.WriteLine($"{key} = {Describe(key, account)}{(scope == SettingScope.Machine ? ", machine only" : string.Empty)}");
            context.Console.WriteLine($"    {description}");
        }

        context.Console.WriteLine($"File associations ({FileTypes.AssociationKeyPrefix}<extension> = command or program, with any arguments before the file; empty for none):");
        foreach ((string extension, string description) in FileTypes.BuiltInTypes)
        {
            context.Console.WriteLine($"{FileTypes.AssociationKeyPrefix}{extension[1..]} = ({description}; built in)");
        }

        foreach ((string extension, string opener, SettingSource source) in FileTypes.GetAssociations(account))
        {
            context.Console.WriteLine($"{FileTypes.AssociationKeyPrefix}{extension[1..]} = {opener} ({FormatSource(source)})");
        }

        context.Console.WriteLine($"Services started at boot: {SystemPaths.ServicesFile} ('service' shows and changes them)");
        context.Console.WriteLine("Files: " + (SystemSettings.CanSave
            ? SystemPaths.SettingsFile + (AccountSettings.GetPath(account) is { } own ? ", yours " + own : string.Empty)
            : "none, no disk is mounted"));
        return CommandResult.Success;
    }

    private static int Get(CommandContext context, string key, bool machine)
    {
        if (!SystemSettings.IsKnown(key))
        {
            return context.Fail(SystemSettings.Validate(key, string.Empty)!);
        }

        context.Console.WriteLine(machine ? SystemSettings.Get(key) : Value(key, context.Caller.Account));
        return CommandResult.Success;
    }

    /// <summary>
    /// Re-reads your own settings file, so changes made by editing it apply, and your cursors
    /// folder, so cursors put there show without logging in again.
    /// </summary>
    private static int ReloadOwn(CommandContext context)
    {
        if (SystemSettings.DenyOwnSettings(context.Caller.Account) is { } denied)
        {
            return context.Fail(denied);
        }

        string path = AccountSettings.GetPath(context.Caller.Account)!;
        foreach (string problem in SystemSettings.ReloadOwn(context.Caller.Account))
        {
            context.Console.WriteError($"{path}: {problem}");
        }

        UI.GUI.Windows.Screen.ReloadCursors(context.Caller.Account);
        context.Console.WriteLine("Reloaded " + path);
        return CommandResult.Success;
    }

    /// <summary>
    /// Re-reads the machine's settings file, so changes made by editing it apply without a reboot.
    /// Problems in it are shown but do not fail the reload: those settings keep their defaults.
    /// </summary>
    private static int ReloadMachine(CommandContext context)
    {
        if (context.Caller.DenyUnlessAdmin("reload the machine's settings") is { } denied)
        {
            return context.Fail(denied);
        }

        if (!SystemSettings.CanSave)
        {
            return context.Fail(FileSystemManager.NoDiskMessage);
        }

        foreach (string problem in SystemSettings.Load())
        {
            context.Console.WriteError($"{SystemPaths.SettingsFile}: {problem}");
        }

        context.Console.WriteLine($"Reloaded {SystemPaths.SettingsFile}");
        return CommandResult.Success;
    }

    /// <summary>The value that applies to the account; for <c>path</c> every folder searched, the machine's and then its own.</summary>
    private static string Value(string key, Account account)
    {
        return key == SystemSettings.PathKey
            ? string.Join(SystemSettings.PathSeparator, SystemSettings.GetPathFolders(account))
            : SystemSettings.Get(key, account);
    }

    /// <summary>The value that applies to the account, and where it comes from.</summary>
    private static string Describe(string key, Account account)
    {
        string source = key == SystemSettings.PathKey
            ? "the machine's, then " + (SystemSettings.GetSource(key, account) == SettingSource.Account ? "yours" : "your programs folder")
            : FormatSource(SystemSettings.GetSource(key, account));
        return $"{(Value(key, account) is { Length: > 0 } value ? value : "none")} ({source})";
    }

    private static string FormatSource(SettingSource source)
    {
        return source switch
        {
            SettingSource.Account => "yours",
            SettingSource.Machine => "machine",
            _ => "default",
        };
    }

    private static int Report(CommandContext context, string? error, string success)
    {
        if (error is not null)
        {
            return context.Fail(error);
        }

        context.Console.WriteLine(success);
        return CommandResult.Success;
    }
}
