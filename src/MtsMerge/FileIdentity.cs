namespace MtsMerge;

/// <summary>
/// 2 つのパスが同じ実体ファイルを指しているかを判定する。
/// 大文字小文字の違い・短いファイル名 (8.3)・ハードリンク・ジャンクション経由でも同一と判定できるよう、
/// ボリュームシリアル番号とファイル ID を比較する。
/// </summary>
internal static class FileIdentity
{
    public static bool IsSameFile(string pathA, string pathB)
    {
        string a = Path.GetFullPath(pathA);
        string b = Path.GetFullPath(pathB);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        var idA = GetId(a);
        var idB = GetId(b);
        return idA is not null && idA == idB;
    }

    private static (uint Volume, ulong Index)? GetId(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (!NativeMethods.GetFileInformationByHandle(handle, out var info)) return null;
            return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
