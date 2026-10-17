using System.Text;

namespace MtsMerge;

internal static class DiskInfo
{
    /// <summary>FAT / FAT32 の 1 ファイルあたりの最大サイズ (4 GiB - 1)。</summary>
    public const long Fat32MaxFileSize = uint.MaxValue;

    /// <summary>指定フォルダのあるボリュームで、現在のユーザーが使える空き容量。取得できなければ null。</summary>
    public static long? GetFreeBytes(string directory)
    {
        try
        {
            string dir = EnsureTrailingSeparator(Path.GetFullPath(directory));
            if (NativeMethods.GetDiskFreeSpaceEx(dir, out ulong free, out _, out _))
                return (long)Math.Min(free, long.MaxValue);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return null;
    }

    /// <summary>ファイルシステム名（NTFS, exFAT, FAT32 など）。取得できなければ null。</summary>
    public static string? GetFileSystemName(string directory)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root)) return null;
            var fsName = new StringBuilder(64);
            if (NativeMethods.GetVolumeInformation(EnsureTrailingSeparator(root), null, 0,
                    out _, out _, out _, fsName, fsName.Capacity))
                return fsName.ToString();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return null;
    }

    /// <summary>ファイルシステムの 1 ファイルあたりの最大サイズ。制限が実質ない場合は null。</summary>
    public static long? GetMaxFileSize(string? fileSystemName) =>
        fileSystemName?.ToUpperInvariant() switch
        {
            "FAT" or "FAT16" or "FAT32" => Fat32MaxFileSize,
            _ => null,
        };

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    public static string FormatBytes(long bytes)
    {
        string[] units = ["バイト", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} {units[0]}" : $"{value:0.00} {units[unit]}";
    }
}
