namespace MtsMerge;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // exe へのドラッグ＆ドロップや「送る」メニューから渡されたファイルも受け付ける
        Application.Run(new MainForm(args));
    }
}
