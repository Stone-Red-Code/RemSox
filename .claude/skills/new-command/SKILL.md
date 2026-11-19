---
name: new-command
description: Add or change a RemSox shell command (the commands shared by the local console, GUI Terminal and Telnet). Use when asked to add, rename, remove or rework a command such as ls, edit, config or spawn.
---

# Adding a shell command

1. **Class** in `RemSox.Kernel/UI/CLI/Commands/` (group related commands in one file, like
   `FileCommands.cs`), `public sealed class XCommand : ICommand`:
   - `Name`, `Description` (one line, shown by `help`), `Usage` (e.g. `cp <source> <destination>`),
     optional `Aliases`, `MinArguments` / `MaxArguments` (the shell prints the usage when the
     count is wrong, so do not re-check it).
   - `ExecuteAsync(CommandContext context)` returns a `CommandResult` code. Use `context.Args`,
     `context.ResolvePath(path)` for paths relative to the shell's directory,
     `context.Console.WriteLine`, `context.ConfirmAsync("...?")`, `context.ReadLineAsync(prompt)`,
     `context.Fail("message")` for errors and `context.UsageError()` for bad arguments.
   - No fields holding run state: one instance serves every shell concurrently.
   - Long loops check `context.Cancellation` and return `CommandResult.Cancelled`.
   - File commands wrap their work in `FileCommandHelpers.Run(context, () => ...)`, which turns
     I/O exceptions into error messages.
   - Full-screen UI: use `context.Console.Screen` (`ITextScreen`, may be null - say so then), as
     `EditCommand`/`TextEditor` do.
2. **Register** it in `Kernel.BeforeRun` (`CommandManager.RegisterCommands([...])` in
   `RemSox.Kernel/Kernel.cs`), near related commands.
3. **Removing** a command: delete the class and its registration, and grep for its name in
   `ShellScript.DefaultInitScript`, help texts and other commands.
4. **Verify** with the `vm` skill: `python tools/vm.py start --build --telnet`, then
   `python tools/vm.py telnet "help <name>"` and run the command with good and bad arguments;
   check a GUI Terminal too if the command draws or reads keys.
