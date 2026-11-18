using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Ext2;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

using RemSox.Kernel.Processes;

namespace RemSox.Kernel.FileSystem;

/// <summary>
/// Registers the filesystem drivers and mounts the first disk at <see cref="MountPoint"/>.
/// </summary>
public static class FileSystemManager
{
    public const string MountPoint = "/mnt";

    private const string FatDriver = "fat";
    private const string Ext2Driver = "ext2";

    private const ulong FirstPartitionSector = 2048;
    private const byte MbrFat32LbaId = 0x0C;

    public static bool IsMounted { get; private set; }

    /// <summary>
    /// Mounts the first partition. A disk without any partition table is treated as blank:
    /// it gets a single MBR partition formatted as FAT32. Disks that already carry a
    /// partition table are never formatted.
    /// </summary>
    public static void Initialize()
    {
        if (!StorageManager.IsEnabled)
        {
            Console.WriteLine("Storage support is disabled.");
            return;
        }

        _ = VfsManager.RegisterFileSystem(FatDriver, new FatFileSystemType());
        _ = VfsManager.RegisterFileSystem(Ext2Driver, new Ext2FileSystemType());

        IBlockDevice? disk = StorageManager.PrimaryDevice;
        if (disk is null)
        {
            Console.WriteLine("No disk found, file system unavailable.");
            return;
        }

        bool formatted = false;

        if (GetPartitions(disk).Count == 0 && IsBlank(disk))
        {
            if (!TryCreateFatPartition(disk))
            {
                return;
            }

            formatted = true;
        }

        List<Partition> partitions = GetPartitions(disk);
        if (partitions.Count == 0)
        {
            Console.WriteLine("No usable partition on " + disk.Name + ".");
            return;
        }

        Partition partition = partitions[0];

        foreach (string driver in (string[])[FatDriver, Ext2Driver])
        {
            if (VfsManager.TryMount(driver, partition, MountFlags.None, MountPoint, out _))
            {
                IsMounted = true;
                Directory.SetCurrentDirectory(MountPoint);
                Console.WriteLine($"Mounted {partition.Name} ({driver}) at {MountPoint}");

                if (formatted)
                {
                    WriteDefaultFiles();
                }

                return;
            }
        }

        Console.WriteLine($"Could not mount {partition.Name}: unknown file system.");
    }

    /// <summary>
    /// Seeds a freshly formatted disk with the demo script the desktop's YesNt launcher runs.
    /// </summary>
    private static void WriteDefaultFiles()
    {
        try
        {
            File.WriteAllLines(YesNtInterpreterProcess.DemoScriptPath, [
                "win_create \"Test\" 320 240",
                "global winId = %win_last_id",
                "win_flush ${winId}",
                "print_line Created window ${winId}",
                "label test:",
                "goto test"
            ]);
        }
        catch (IOException ex)
        {
            Console.WriteLine("Could not write default files: " + ex.Message);
        }
    }

    /// <summary>
    /// Returns the partitions on <paramref name="disk"/>. StorageManager.Partitions lists those of
    /// every block device, the boot CD's included since Cosmos 3.0.96, so on a blank disk it would
    /// offer the CD's partition.
    /// </summary>
    private static List<Partition> GetPartitions(IBlockDevice disk)
    {
        return [.. StorageManager.Partitions.Where(partition => ReferenceEquals(partition.Host, disk))];
    }

    private static bool IsBlank(IBlockDevice disk)
    {
        return !Mbr.IsMbr(disk) && !Gpt.IsGpt(disk);
    }

    private static bool TryCreateFatPartition(IBlockDevice disk)
    {
        if (disk.BlockCount <= FirstPartitionSector)
        {
            Console.WriteLine(disk.Name + " is too small to partition.");
            return false;
        }

        Console.WriteLine($"Formatting blank disk {disk.Name} as FAT32...");

        Mbr.Create(disk);

        if (!PartitionManager.Create(disk, FirstPartitionSector, disk.BlockCount - FirstPartitionSector, MbrFat32LbaId, Gpt.BasicDataPartitionType))
        {
            Console.WriteLine("Failed to create partition.");
            return false;
        }

        StorageManager.RescanPartitions(disk);

        List<Partition> partitions = GetPartitions(disk);
        if (partitions.Count == 0 ||
            !VfsManager.TryFormat(FatDriver, partitions[0], new FatFormatOptions { Type = FatType.Fat32, VolumeLabel = "REMSOX     " }))
        {
            Console.WriteLine("Failed to format partition.");
            return false;
        }

        return true;
    }
}
