using Microsoft.UI.Xaml;
using ToDoList.Core.Storage;

namespace ToDoList.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        UnhandledException += (_, e) => LogFailure(e.Exception);
        DebugSettings.XamlResourceReferenceFailed += (_, e) => LogFailure(new InvalidOperationException(e.Message));
        try { InitializeComponent(); }
        catch (Exception exception) { LogFailure(exception); throw; }
    }

    private static void LogFailure(Exception exception)
    {
        try
        {
            var paths = new AppPaths();
            Directory.CreateDirectory(paths.DataDirectory);
            File.AppendAllText(Path.Combine(paths.DataDirectory, "startup.log"),
                $"{DateTimeOffset.Now:O}\n{exception}\n\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Contains("--clear-demo-data", StringComparer.Ordinal))
        {
            var paths = new AppPaths();
            try
            {
                var count = SampleDataCleaner.Clean(paths);
                File.WriteAllText(Path.Combine(paths.DataDirectory, "cleanup-result.txt"),
                    $"已清理 {count} 条未修改的示例记录（含备份）。用户新增或修改的计划已保留。");
                Environment.Exit(0);
            }
            catch (Exception exception)
            {
                try { File.WriteAllText(Path.Combine(paths.DataDirectory, "cleanup-result.txt"), "清理失败，请关闭程序后重试：" + exception.Message); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Environment.Exit(1);
            }
            return;
        }
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception) { LogFailure(exception); throw; }
    }
}
