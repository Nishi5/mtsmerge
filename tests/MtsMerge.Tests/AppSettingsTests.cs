namespace MtsMerge.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mtsmerge-settings-").FullName;
    private string SettingsPath => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_ReturnsDefaultsWhenFileMissing()
    {
        var s = AppSettings.Load(SettingsPath);
        Assert.True(s.MatchTimestampToFirstFile);
        Assert.True(s.OpenFolderWhenDone);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        new AppSettings { MatchTimestampToFirstFile = false, OpenFolderWhenDone = false }.Save(SettingsPath);

        var s = AppSettings.Load(SettingsPath);
        Assert.False(s.MatchTimestampToFirstFile);
        Assert.False(s.OpenFolderWhenDone);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenFileCorrupted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ broken");

        var s = AppSettings.Load(SettingsPath);
        Assert.True(s.MatchTimestampToFirstFile);
        Assert.True(s.OpenFolderWhenDone);
    }
}
