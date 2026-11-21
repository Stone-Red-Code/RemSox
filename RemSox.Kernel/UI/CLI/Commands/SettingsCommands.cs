using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ConfigCommand : ICommand
{
    public string Name => "config";

    public string Description => "Show or change the settings kept across reboots";

    public string Usage => "config [list | get <key> | set <key> <value, \"\" for none> | reset <key> | reload]";

    public int MaxArguments => 3;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;
        string action = args.Count > 0 ? args[0].ToLowerInvariant() : "list";

        // Anyone may look; SystemSettings itself refuses set and reset without admin rights.
        if (action == "reload" && context.Caller.DenyUnlessAdmin("reload the settings") is { } denied)
        {
            return Task.FromResult(context.Fail(denied));
        }

        int result = (action, args.Count) switch
        {
            ("list", 1) or (_, 0) => List(context),
            ("get", 2) => Get(context, args[1].ToLowerInvariant()),
            ("set", 3) => Report(context, SystemSettings.Set(context.Caller, args[1].ToLowerInvariant(), args[2]), $"{args[1]} = {SystemSettings.Get(args[1].ToLowerInvariant())}"),
            ("reset", 2) => Report(context, SystemSettings.Reset(context.Caller, args[1].ToLowerInvariant()), SystemSettings.IsSet(args[1].ToLowerInvariant())
                ? $"{args[1]} reset to {SystemSettings.Get(args[1].ToLowerInvariant())}"
                : $"{args[1]} removed (it has no default)"),
            ("reload", 1) => Reload(context),
            _ => context.UsageError(),
        };

        return Task.FromResult(result);
    }

    // Only without a disk: otherwise the file holds every setting (see SystemSettings.Load).
    private const string NotInFile = " (built-in, not in the settings file)";

    private static int List(CommandContext context)
    {
        foreach ((string key, string _, string description, SettingScope _) in SystemSettings.Known)
        {
            string source = SystemSettings.IsSet(key) ? string.Empty : NotInFile;
            context.Console.WriteLine($"{key} = {SystemSettings.Get(key)}{source}");
            context.Console.WriteLine($"    {description}");
        }

        context.Console.WriteLine($"File associations ({FileTypes.AssociationKeyPrefix}<extension> = command or program, empty for none):");
        context.Console.WriteLine("    .rsh scripts and .rsl shortcuts are built in and have none.");
        foreach ((string extension, string opener, SettingSource source) in FileTypes.GetAssociations(context.Caller.Account))
        {
            context.Console.WriteLine($"{FileTypes.AssociationKeyPrefix}{extension[1..]} = {opener}{(source == SettingSource.Default ? NotInFile : string.Empty)}");
        }

        context.Console.WriteLine($"Services started at boot: {SystemPaths.ServicesFile} ('service' shows and changes them)");
        context.Console.WriteLine("File: " + (SystemSettings.CanSave ? SystemPaths.SettingsFile : "none, no disk is mounted"));
        return CommandResult.Success;
    }

    private static int Get(CommandContext context, string key)
    {
        if (!SystemSettings.IsKnown(key))
        {
            return context.Fail(SystemSettings.Validate(key, string.Empty)!);
        }

        context.Console.WriteLine(SystemSettings.Get(key));
        return CommandResult.Success;
    }

    /// <summary>
    /// Re-reads the settings file, so changes made by editing it apply without a reboot. Problems
    /// in it are shown but do not fail the reload: those settings keep their defaults.
    /// </summary>
    private static int Reload(CommandContext context)
    {
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
