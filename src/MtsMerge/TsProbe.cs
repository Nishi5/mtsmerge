namespace MtsMerge;

/// <summary>ファイル先頭の同期バイト (0x47) からパケット形式を判定する。</summary>
internal static class TsProbe
{
    private const byte SyncByte = 0x47;
    private const int ProbePackets = 3;

    public static TsFormat Detect(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Detect(fs);
        }
        catch (IOException) { return TsFormat.Unknown; }
        catch (UnauthorizedAccessException) { return TsFormat.Unknown; }
    }

    public static TsFormat Detect(Stream stream)
    {
        Span<byte> buf = stackalloc byte[192 * ProbePackets];
        int n = stream.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        buf = buf[..n];

        // 192 バイト形式は 4 バイトのタイムコードの後に同期バイトが来る
        if (HasSync(buf, offset: 4, packetSize: 192)) return TsFormat.M2ts192;
        if (HasSync(buf, offset: 0, packetSize: 188)) return TsFormat.Ts188;
        return TsFormat.Unknown;
    }

    private static bool HasSync(ReadOnlySpan<byte> buf, int offset, int packetSize)
    {
        for (int i = 0; i < ProbePackets; i++)
        {
            int pos = offset + i * packetSize;
            if (pos >= buf.Length || buf[pos] != SyncByte) return false;
        }
        return true;
    }
}
