using System.Diagnostics;

namespace MtsMerge;

internal sealed record MergeProgress(long BytesDone, long BytesTotal, int FileIndex, int FileCount);

/// <summary>
/// MPEG-TS ファイルをバイナリ連結する。
/// 入力ファイルは読み取り専用で開き、出力はまず同じフォルダの一時ファイルに書き込んでから
/// 完了・検証後にリネームするため、途中で失敗しても入力・既存ファイルは壊れない。
/// </summary>
internal static class MergeEngine
{
    private const int BufferSize = 4 * 1024 * 1024;
    private const int ReportIntervalMs = 100;

    public static async Task MergeAsync(
        IReadOnlyList<InputFile> inputs,
        string outputPath,
        bool overwrite,
        DateTime? creationTime,
        DateTime? lastWriteTime,
        IProgress<MergeProgress>? progress,
        CancellationToken ct)
    {
        if (inputs.Count == 0) throw new ArgumentException("入力ファイルがありません。", nameof(inputs));

        string output = Path.GetFullPath(outputPath);
        EnsureNotInput(inputs, output);
        if (!overwrite && File.Exists(output))
            throw new IOException($"出力ファイルが既に存在します: {output}");

        string directory = Path.GetDirectoryName(output)
            ?? throw new IOException($"出力先フォルダが不正です: {output}");
        string temp = Path.Combine(directory, $"{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");

        long total = inputs.Sum(f => f.Size);
        long done = 0;
        var buffer = new byte[BufferSize];
        var sw = Stopwatch.StartNew();
        long lastReport = 0;

        try
        {
            // FileMode.CreateNew: 既存ファイルは絶対に開かない
            await using (var dst = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                // 先に領域を確保して、容量不足なら書き込み前に失敗させる
                dst.SetLength(total);

                for (int i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    await using var src = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (src.Length != input.Size)
                        throw new IOException($"入力ファイルのサイズが追加時から変わっています: {input.Path}");

                    long remaining = input.Size;
                    while (remaining > 0)
                    {
                        int read = await src.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                        if (read == 0) throw new EndOfStreamException($"入力ファイルを最後まで読めませんでした: {input.Path}");
                        await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                        remaining -= read;
                        done += read;

                        if (sw.ElapsedMilliseconds - lastReport >= ReportIntervalMs)
                        {
                            lastReport = sw.ElapsedMilliseconds;
                            progress?.Report(new MergeProgress(done, total, i, inputs.Count));
                        }
                    }
                }

                await dst.FlushAsync(ct);
                dst.Flush(flushToDisk: true);
            }

            long written = new FileInfo(temp).Length;
            if (written != total)
                throw new IOException($"出力サイズが一致しません（期待値 {total:N0} バイト / 実際 {written:N0} バイト）。");

            if (creationTime is { } c) File.SetCreationTime(temp, c);
            if (lastWriteTime is { } w) File.SetLastWriteTime(temp, w);

            ct.ThrowIfCancellationRequested();
            // 書き込み中に状況が変わっていないか最終確認してから置き換える
            EnsureNotInput(inputs, output);
            File.Move(temp, output, overwrite);
            progress?.Report(new MergeProgress(total, total, inputs.Count - 1, inputs.Count));
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void EnsureNotInput(IReadOnlyList<InputFile> inputs, string output)
    {
        foreach (var input in inputs)
        {
            if (FileIdentity.IsSameFile(input.Path, output))
                throw new InvalidOperationException($"出力先が入力ファイルと同じです: {input.Path}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
