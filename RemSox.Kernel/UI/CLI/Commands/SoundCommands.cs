using Cosmos.Kernel.System.Audio;
using Cosmos.Kernel.System.FileSystem;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shared checks for the sound commands.
/// </summary>
internal static class SoundCommandHelpers
{
    /// <summary>Returns the primary audio output, or prints why there is none.</summary>
    public static AudioDevice? GetOutput(CommandContext context)
    {
        if (!AudioManager.IsEnabled)
        {
            _ = context.Fail("Audio support is not compiled into this kernel.");
            return null;
        }

        AudioDevice? output = AudioManager.Primary;
        if (output is null)
        {
            _ = context.Fail("No sound card found. In QEMU, attach one with: -device intel-hda -device hda-duplex");
        }

        return output;
    }
}

public sealed class BeepCommand : ICommand
{
    private const int DefaultFrequency = 800;
    private const int DefaultDuration = 200;
    private const int MaxDuration = 5000;

    public string Name => "beep";

    public string Description => "Play a tone through the sound card";

    public string Usage => "beep [hz 37-32767] [ms 1-" + MaxDuration + "]";

    public int MaxArguments => 2;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        int frequency = DefaultFrequency;
        int duration = DefaultDuration;

        if ((context.Args.Count > 0 && (!int.TryParse(context.Args[0], out frequency) || frequency is < 37 or > 32767)) ||
            (context.Args.Count > 1 && (!int.TryParse(context.Args[1], out duration) || duration is < 1 or > MaxDuration)))
        {
            return Task.FromResult(context.UsageError());
        }

        AudioDevice? output = SoundCommandHelpers.GetOutput(context);
        if (output is null)
        {
            return Task.FromResult(CommandResult.Failure);
        }

        if (output.Player is not null)
        {
            return Task.FromResult(context.Fail("The sound card is busy playing a file (play-stop ends it)."));
        }

        // Cosmos plugs Console.Beep onto the primary audio output; it returns once the tone has played.
#pragma warning disable CA1416 // Windows-only in the BCL, implemented by Cosmos.
        Console.Beep(frequency, duration);
#pragma warning restore CA1416
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class PlayCommand : ICommand
{
    public string Name => "play";

    public string Description => "Play a 16-bit stereo PCM .wav file in the background";

    public string Usage => "play <file.wav>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        AudioDevice? output = SoundCommandHelpers.GetOutput(context);
        if (output is null)
        {
            return Task.FromResult(CommandResult.Failure);
        }

        if (FileCommandHelpers.RequireDisk(context) is { } noDisk)
        {
            return Task.FromResult(noDisk);
        }

        string path = context.ResolvePath(context.Args[0]);
        if (!VfsManager.TryOpenFile(path, out IVfsFileHandle? file))
        {
            return Task.FromResult(context.Fail("File not found: " + path));
        }

        if (!WaveAudioStream.TryOpen(file, out WaveAudioStream? song))
        {
            file.Dispose();
            return Task.FromResult(context.Fail("Not a PCM .wav file: " + path));
        }

        // The playback thread reads the file as it plays and disposes of it when it ends.
        if (!AudioManager.TryStartPlayback(song, file, out AudioPlayback? _))
        {
            file.Dispose();
            return Task.FromResult(context.Fail("Could not play it: the sound card is busy, or it cannot play this format (it needs 16-bit stereo; convert with: ffmpeg -i in -ac 2 -ar 48000 -c:a pcm_s16le out.wav)."));
        }

        context.Console.WriteLine($"Playing {Path.GetFileName(path)} ({song.Length / song.SampleRate} s, {song.SampleRate} Hz). 'play-stop' stops it.");
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class StopPlaybackCommand : ICommand
{
    public string Name => "play-stop";

    public string Description => "Stop the sound that is playing";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        AudioPlayer? player = AudioManager.IsEnabled ? AudioManager.Primary?.Player : null;
        if (player is null)
        {
            return Task.FromResult(context.Fail("Nothing is playing."));
        }

        player.RequestStop();
        context.Console.WriteLine("Stopped.");
        return Task.FromResult(CommandResult.Success);
    }
}
