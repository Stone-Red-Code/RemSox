using Cosmos.Kernel.System.Audio;
using Cosmos.Kernel.System.FileSystem;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shared checks for the sound commands.
/// </summary>
internal static class SoundCommandHelpers
{
    /// <summary>Returns the primary audio output, or prints why there is none.</summary>
    public static AudioDevice? GetOutput(Action<string> printLine)
    {
        if (!AudioManager.IsEnabled)
        {
            printLine("Audio support is not compiled into this kernel.");
            return null;
        }

        AudioDevice? output = AudioManager.Primary;
        if (output is null)
        {
            printLine("No sound card found. In QEMU, attach one with: -device intel-hda -device hda-duplex");
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

    public string Description => "Play a tone through the sound card (beep [hz 37-32767] [ms 1-5000])";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);
        int frequency = DefaultFrequency;
        int duration = DefaultDuration;

        if ((args.Count > 0 && (!int.TryParse(args[0], out frequency) || frequency is < 37 or > 32767)) ||
            (args.Count > 1 && (!int.TryParse(args[1], out duration) || duration is < 1 or > MaxDuration)))
        {
            printLine("Usage: beep [hz 37-32767] [ms 1-" + MaxDuration + "]");
            return Task.CompletedTask;
        }

        AudioDevice? output = SoundCommandHelpers.GetOutput(printLine);
        if (output is null)
        {
            return Task.CompletedTask;
        }

        if (output.Player is not null)
        {
            printLine("The sound card is busy playing a file (play-stop ends it).");
            return Task.CompletedTask;
        }

        // Cosmos plugs Console.Beep onto the primary audio output; it returns once the tone has played.
#pragma warning disable CA1416 // Windows-only in the BCL, implemented by Cosmos.
        Console.Beep(frequency, duration);
#pragma warning restore CA1416
        return Task.CompletedTask;
    }
}

public sealed class PlayCommand : ICommand
{
    public string Name => "play";

    public string Description => "Play a 16-bit stereo PCM .wav file in the background (play <file.wav>)";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);
        if (args.Count != 1)
        {
            printLine("Usage: play <file.wav>");
            return Task.CompletedTask;
        }

        AudioDevice? output = SoundCommandHelpers.GetOutput(printLine);
        if (output is null)
        {
            return Task.CompletedTask;
        }

        string path = Path.GetFullPath(args[0]);
        if (!VfsManager.TryOpenFile(path, out IVfsFileHandle? file))
        {
            printLine("File not found: " + path);
            return Task.CompletedTask;
        }

        if (!WaveAudioStream.TryOpen(file, out WaveAudioStream? song))
        {
            printLine("Not a PCM .wav file: " + path);
            file.Dispose();
            return Task.CompletedTask;
        }

        // The playback thread reads the file as it plays and disposes of it when it ends.
        if (!AudioManager.TryStartPlayback(song, file, out AudioPlayback? _))
        {
            printLine("Could not play it: the sound card is busy, or it cannot play this format (it needs 16-bit stereo; convert with: ffmpeg -i in -ac 2 -ar 48000 -c:a pcm_s16le out.wav).");
            file.Dispose();
            return Task.CompletedTask;
        }

        printLine($"Playing {Path.GetFileName(path)} ({song.Length / song.SampleRate} s, {song.SampleRate} Hz). 'play-stop' stops it.");
        return Task.CompletedTask;
    }
}

public sealed class StopPlaybackCommand : ICommand
{
    public string Name => "play-stop";

    public string Description => "Stop the sound that is playing";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        AudioPlayer? player = AudioManager.IsEnabled ? AudioManager.Primary?.Player : null;
        if (player is null)
        {
            printLine("Nothing is playing.");
            return Task.CompletedTask;
        }

        player.RequestStop();
        printLine("Stopped.");
        return Task.CompletedTask;
    }
}
