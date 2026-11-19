using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ConfigCommand : ICommand
{
    public string Name => "config";

    public string Description => "Show or change the settings kept across reboots";

    public string Usage => "config [list | get <key> | set <key> <value> | reset <key>]";

    public int MaxArguments => 3;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;
        string action = args.Count > 0 ? args[0].ToLowerInvariant() : "list";

        int result = (action, args.Count) switch
        {
            ("list", 1) or (_, 0) => List(context),
            ("get", 2) => Get(context, args[1].ToLowerInvariant()),
            ("set", 3) => Report(context, SystemSettings.Set(args[1].ToLowerInvariant(), args[2]), $"{args[1]} = {args[2]}"),
            ("reset", 2) => Report(context, SystemSettings.Reset(args[1].ToLowerInvariant()), $"{args[1]} reset to {SystemSettings.Get(args[1].ToLowerInvariant())}"),
            _ => context.UsageError(),
        };

        return Task.FromResult(result);
    }

    private static int List(CommandContext context)
    {
        foreach ((string key, string _, string description) in SystemSettings.Known)
        {
            string source = SystemSettings.IsSet(key) ? string.Empty : " (default)";
            context.Console.WriteLine($"{key} = {SystemSettings.Get(key)}{source}");
            context.Console.WriteLine($"    {description}");
        }

        context.Console.WriteLine($"File associations ({FileTypes.AssociationKeyPrefix}<extension> = command, program or {FileTypes.NoAssociation}):");
        context.Console.WriteLine("    .rsh scripts and .rsl shortcuts are built in and have none.");
        foreach ((string extension, string opener, bool isDefault) in FileTypes.GetAssociations())
        {
            context.Console.WriteLine($"{FileTypes.AssociationKeyPrefix}{extension[1..]} = {opener}{(isDefault ? " (default)" : string.Empty)}");
        }

        context.Console.WriteLine($"Startup script: {SystemPaths.StartupScript} ('autostart' lists its commands)");
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

public sealed class AutostartCommand : ICommand
{
    public string Name => "autostart";

    public string Description => "List or edit the startup script, " + SystemPaths.StartupScript;

    public string Usage => "autostart [list | add <command>... | remove <number>]";

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;
        string action = args.Count > 0 ? args[0].ToLowerInvariant() : "list";

        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            ShellScript.EnsureInitScript();

            return action switch
            {
                "list" when args.Count <= 1 => List(context),
                "add" when args.Count >= 2 => Add(context),
                "remove" when args.Count == 2 => Remove(context, args[1]),
                _ => context.UsageError(),
            };
        }));
    }

    // Only without a disk: otherwise the file holds every setting (see SystemSettings.Load).
    private const string NotInFile = " (built-in, not in the settings file)";

    private static int List(CommandContext context)
    {
        List<string> commands = ShellScript.ReadCommands(SystemPaths.StartupScript);

        if (commands.Count == 0)
        {
            context.Console.WriteLine($"{SystemPaths.StartupScript} runs no commands. Add one with: autostart add <command>");
            return CommandResult.Success;
        }

        context.Console.WriteLine(SystemPaths.StartupScript + " runs at boot:");
        for (int i = 0; i < commands.Count; i++)
        {
            context.Console.WriteLine($"  {i + 1}. {commands[i]}");
        }

        return CommandResult.Success;
    }

    private static int Add(CommandContext context)
    {
        // The command as typed after "add", so its own quoting is kept.
        string raw = context.RawArguments;
        string command = raw[(raw.IndexOf(context.Args[0], StringComparison.OrdinalIgnoreCase) + context.Args[0].Length)..].Trim();

        // The startup script runs in the home folder, so a file it names is looked up from there.
        if (CommandLine.Parse(command) is not { } parsed
            || (CommandManager.Find(parsed.Name) is null && Launcher.Resolve(parsed.Name, parsed.Arguments, SystemPaths.HomeFolder) is null))
        {
            return context.Fail($"\"{command}\" does not start with a command, program or file that can be opened.");
        }

        ShellScript.AddCommand(SystemPaths.StartupScript, command);
        context.Console.WriteLine($"Added: {command} (runs at the next boot)");
        return CommandResult.Success;
    }

    private static int Remove(CommandContext context, string number)
    {
        if (!int.TryParse(number, out int index))
        {
            return context.UsageError($"Not a number: {number}");
        }

        if (ShellScript.RemoveCommand(SystemPaths.StartupScript, index - 1) is not { } removed)
        {
            return context.Fail($"There is no startup command {index}.");
        }

        context.Console.WriteLine("Removed: " + removed);
        return CommandResult.Success;
    }
}
