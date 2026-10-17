using System.Text.Json;

namespace MtsMerge;

/// <summary>
/// アプリを閉じても保持する設定。%APPDATA%\MtsMerge\settings.json に保存する。
/// 読み書きに失敗しても動作に支障がないよう、失敗時は既定値を使い例外は外に出さない。
/// </summary>
internal sealed class AppSettings
{
    public bool MatchTimestampToFirstFile { get; set; } = true;
    public bool OpenFolderWhenDone { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MtsMerge", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            string json = File.ReadAllText(path ?? DefaultPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 書き込み途中で中断しても設定ファイルが壊れないよう、一時ファイル経由で置き換える
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }
}
