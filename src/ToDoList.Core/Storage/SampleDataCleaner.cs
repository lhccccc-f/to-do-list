using Microsoft.Data.Sqlite;

namespace ToDoList.Core.Storage;

/// <summary>Removes unchanged sample rows only. User edits and user-created plans survive.</summary>
public static class SampleDataCleaner
{
    public static int Clean(AppPaths paths)
    {
        paths.EnsureWritable();
        using var session = new FileStream(Path.Combine(paths.DataDirectory, ".session.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var removed = 0;
        foreach (var file in new[] { paths.DatabaseFile, paths.BackupFile })
        {
            if (!File.Exists(file)) continue;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = file, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            connection.Open();
            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SampleEntries'";
            if (Convert.ToInt64(exists.ExecuteScalar()) == 0) continue;
            using var transaction = connection.BeginTransaction();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM Plans WHERE EXISTS (
                    SELECT 1 FROM SampleEntries s WHERE s.Id = Plans.Id
                    AND s.PlanDate = Plans.PlanDate AND s.Content = Plans.Content AND s.Duration = Plans.Duration
                );
                """;
            removed += delete.ExecuteNonQuery();
            using var forget = connection.CreateCommand();
            forget.Transaction = transaction;
            forget.CommandText = "DROP TABLE SampleEntries";
            forget.ExecuteNonQuery();
            transaction.Commit();
        }
        return removed;
    }
}
