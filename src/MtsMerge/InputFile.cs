namespace MtsMerge;

/// <summary>トランスポートストリームのパケット形式。</summary>
internal enum TsFormat
{
    Unknown,
    /// <summary>188 バイトパケット（.ts など）。</summary>
    Ts188,
    /// <summary>192 バイトパケット（AVCHD の .MTS / .m2ts）。</summary>
    M2ts192,
}

internal sealed class InputFile
{
    public required string Path { get; init; }
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime CreationTime { get; set; }
    public TsFormat Format { get; set; }

    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";

    public static InputFile Load(string path)
    {
        var fi = new FileInfo(path);
        return new InputFile
        {
            Path = fi.FullName,
            Size = fi.Length,
            LastWriteTime = fi.LastWriteTime,
            CreationTime = fi.CreationTime,
            Format = TsProbe.Detect(fi.FullName),
        };
    }

    /// <summary>ファイル情報を再取得する。ファイルが存在しない場合は false。</summary>
    public bool Refresh()
    {
        var fi = new FileInfo(Path);
        if (!fi.Exists) return false;
        Size = fi.Length;
        LastWriteTime = fi.LastWriteTime;
        CreationTime = fi.CreationTime;
        return true;
    }

    public static string FormatName(TsFormat format) => format switch
    {
        TsFormat.Ts188 => "TS (188)",
        TsFormat.M2ts192 => "M2TS (192)",
        _ => "不明",
    };
}
