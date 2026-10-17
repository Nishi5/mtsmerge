namespace MtsMerge.Tests;

public sealed class MergeEngineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mtsmerge-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private InputFile CreateInput(string name, int packets, byte fill)
    {
        string path = Path.Combine(_dir, name);
        var data = new byte[packets * 192];
        for (int i = 0; i < packets; i++)
        {
            data.AsSpan(i * 192, 192).Fill(fill);
            data[i * 192 + 4] = 0x47;
        }
        File.WriteAllBytes(path, data);
        return InputFile.Load(path);
    }

    [Fact]
    public async Task Merge_ConcatenatesInOrder_AndLeavesInputsUntouched()
    {
        var a = CreateInput("00001.MTS", 100, 1);
        var b = CreateInput("00002.MTS", 50, 2);
        var c = CreateInput("00003.MTS", 10, 3);
        var originals = new[] { a, b, c }.Select(f => File.ReadAllBytes(f.Path)).ToArray();
        string output = Path.Combine(_dir, "00001-merge.MTS");

        await MergeEngine.MergeAsync([b, a, c], output, overwrite: false, null, null, null, CancellationToken.None);

        byte[] expected = [.. originals[1], .. originals[0], .. originals[2]];
        Assert.Equal(expected, File.ReadAllBytes(output));
        Assert.Equal(originals[0], File.ReadAllBytes(a.Path));
        Assert.Equal(originals[1], File.ReadAllBytes(b.Path));
        Assert.Equal(originals[2], File.ReadAllBytes(c.Path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Merge_RefusesToWriteOverAnInputFile()
    {
        var a = CreateInput("00001.MTS", 10, 1);
        var b = CreateInput("00002.MTS", 10, 2);
        byte[] before = File.ReadAllBytes(a.Path);

        // 大文字小文字違いでも同一ファイルとして扱う
        string output = Path.Combine(_dir, "00001.mts");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MergeEngine.MergeAsync([a, b], output, overwrite: true, null, null, null, CancellationToken.None));

        Assert.Equal(before, File.ReadAllBytes(a.Path));
    }

    [Fact]
    public async Task Merge_RefusesHardLinkToInput()
    {
        var a = CreateInput("00001.MTS", 10, 1);
        var b = CreateInput("00002.MTS", 10, 2);
        string link = Path.Combine(_dir, "link.MTS");
        Assert.True(CreateHardLink(link, a.Path, IntPtr.Zero));
        byte[] before = File.ReadAllBytes(a.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MergeEngine.MergeAsync([a, b], link, overwrite: true, null, null, null, CancellationToken.None));

        Assert.Equal(before, File.ReadAllBytes(a.Path));
    }

    [Fact]
    public async Task Merge_DoesNotOverwriteExistingFileUnlessAllowed()
    {
        var a = CreateInput("00001.MTS", 10, 1);
        var b = CreateInput("00002.MTS", 10, 2);
        string output = Path.Combine(_dir, "out.MTS");
        File.WriteAllText(output, "existing");

        await Assert.ThrowsAsync<IOException>(() =>
            MergeEngine.MergeAsync([a, b], output, overwrite: false, null, null, null, CancellationToken.None));
        Assert.Equal("existing", File.ReadAllText(output));

        await MergeEngine.MergeAsync([a, b], output, overwrite: true, null, null, null, CancellationToken.None);
        Assert.Equal(a.Size + b.Size, new FileInfo(output).Length);
    }

    [Fact]
    public async Task Merge_Cancelled_RemovesTemporaryFileAndKeepsExistingOutput()
    {
        var a = CreateInput("00001.MTS", 100, 1);
        var b = CreateInput("00002.MTS", 100, 2);
        string output = Path.Combine(_dir, "out.MTS");
        File.WriteAllText(output, "existing");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MergeEngine.MergeAsync([a, b], output, overwrite: true, null, null, null, cts.Token));

        Assert.Equal("existing", File.ReadAllText(output));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Merge_FailsWhenInputChangedAfterAdding()
    {
        var a = CreateInput("00001.MTS", 10, 1);
        var b = CreateInput("00002.MTS", 10, 2);
        File.AppendAllText(b.Path, "x");
        string output = Path.Combine(_dir, "out.MTS");

        await Assert.ThrowsAsync<IOException>(() =>
            MergeEngine.MergeAsync([a, b], output, overwrite: false, null, null, null, CancellationToken.None));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Merge_AppliesTimestamps()
    {
        var a = CreateInput("00001.MTS", 10, 1);
        var b = CreateInput("00002.MTS", 10, 2);
        var stamp = new DateTime(2020, 5, 1, 12, 34, 56);
        string output = Path.Combine(_dir, "out.MTS");

        await MergeEngine.MergeAsync([a, b], output, false, stamp, stamp, null, CancellationToken.None);

        Assert.Equal(stamp, File.GetLastWriteTime(output));
        Assert.Equal(stamp, File.GetCreationTime(output));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
