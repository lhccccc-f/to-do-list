namespace ToDoList.Core.Storage;

public sealed class AppPaths
{
    // Resolve against the executable, never the current working directory.
    public string Root { get; }
    public string DataDirectory => Path.Combine(Root, "Data");
    public string DatabaseFile => Path.Combine(DataDirectory, "plans.db");
    public string BackupFile => Path.Combine(DataDirectory, "plans.backup.db");
    public string ExportDirectory => Path.Combine(Root, "Exports");
    public string FontFile => Path.Combine(Root, "Assets", "Fonts", "LXGWNeoXiHei.ttf");

    public AppPaths(string? root = null) => Root = Path.GetFullPath(root ?? AppContext.BaseDirectory);

    public void EnsureWritable()
    {
        Directory.CreateDirectory(DataDirectory);
        var probe = Path.Combine(DataDirectory, $".write-probe-{Guid.NewGuid():N}");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   1, FileOptions.DeleteOnClose)) { }
    }
}
