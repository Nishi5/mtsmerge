namespace MtsMerge.Tests;

public sealed class TsProbeTests
{
    private static byte[] Packets(int packetSize, int syncOffset, int count)
    {
        var data = new byte[packetSize * count];
        for (int i = 0; i < count; i++) data[i * packetSize + syncOffset] = 0x47;
        return data;
    }

    [Fact]
    public void Detects192BytePackets() =>
        Assert.Equal(TsFormat.M2ts192, TsProbe.Detect(new MemoryStream(Packets(192, 4, 5))));

    [Fact]
    public void Detects188BytePackets() =>
        Assert.Equal(TsFormat.Ts188, TsProbe.Detect(new MemoryStream(Packets(188, 0, 5))));

    [Fact]
    public void UnknownForOtherData() =>
        Assert.Equal(TsFormat.Unknown, TsProbe.Detect(new MemoryStream(new byte[1000])));

    [Fact]
    public void UnknownForTooShortData() =>
        Assert.Equal(TsFormat.Unknown, TsProbe.Detect(new MemoryStream(Packets(192, 4, 1))));

    [Theory]
    [InlineData("FAT32", DiskInfo.Fat32MaxFileSize)]
    [InlineData("fat", DiskInfo.Fat32MaxFileSize)]
    [InlineData("NTFS", null)]
    [InlineData("exFAT", null)]
    public void MaxFileSizeByFileSystem(string fs, long? expected) =>
        Assert.Equal(expected, DiskInfo.GetMaxFileSize(fs));
}
