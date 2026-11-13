using System.Globalization;
using Microsoft.Data.Sqlite;
using ToDoList.Core.Models;

namespace ToDoList.Core.Storage;

public sealed class PlanRepository : IDisposable
{
    private readonly SqliteConnection _connection;

    public PlanRepository(string databaseFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databaseFile),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        _connection.Open();
        using (var version = _connection.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) > 1)
            {
                _connection.Dispose();
                throw new InvalidOperationException("此计划数据库由更新版本创建，请使用更新版本的程序打开。");
            }
        }
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = FULL;
            PRAGMA busy_timeout = 5000;
            CREATE TABLE IF NOT EXISTS Plans (
                Id TEXT NOT NULL PRIMARY KEY,
                PlanDate TEXT NOT NULL,
                Position INTEGER NOT NULL,
                Content TEXT NOT NULL,
                Duration TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Plans_Date_Position ON Plans(PlanDate, Position);
            CREATE TABLE IF NOT EXISTS Settings (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL);
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PlanItem> LoadDay(DateOnly date)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, Content, Duration FROM Plans WHERE PlanDate = $date ORDER BY Position, Id";
        command.Parameters.AddWithValue("$date", DateKey(date));
        using var reader = command.ExecuteReader();
        var items = new List<PlanItem>();
        while (reader.Read())
            items.Add(new PlanItem { Id = reader.GetString(0), Content = reader.GetString(1),
                Duration = reader.GetString(2), Number = items.Count + 1 });
        return items;
    }

    // Delete + insert is a single transaction: a failed save preserves the previous day.
    public void SaveDay(DateOnly date, IEnumerable<PlanItem> items)
    {
        var snapshot = items.Select(item => item.Snapshot()).ToArray();
        using var transaction = _connection.BeginTransaction();
        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Plans WHERE PlanDate = $date";
            delete.Parameters.AddWithValue("$date", DateKey(date));
            delete.ExecuteNonQuery();
        }
        for (var i = 0; i < snapshot.Length; i++)
        {
            var item = snapshot[i];
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO Plans(Id,PlanDate,Position,Content,Duration) VALUES($id,$date,$position,$content,$duration)";
            insert.Parameters.AddWithValue("$id", item.Id);
            insert.Parameters.AddWithValue("$date", DateKey(date));
            insert.Parameters.AddWithValue("$position", i);
            insert.Parameters.AddWithValue("$content", item.Content);
            insert.Parameters.AddWithValue("$duration", item.Duration);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyDictionary<DateOnly, int> GetDayCounts(DateOnly from, DateOnly to)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT PlanDate, Content, Duration FROM Plans
            WHERE PlanDate >= $from AND PlanDate <= $to
            """;
        command.Parameters.AddWithValue("$from", DateKey(from));
        command.Parameters.AddWithValue("$to", DateKey(to));
        using var reader = command.ExecuteReader();
        var result = new Dictionary<DateOnly, int>();
        while (reader.Read())
        {
            if (string.IsNullOrWhiteSpace(reader.GetString(1)) && string.IsNullOrWhiteSpace(reader.GetString(2))) continue;
            var date = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            result[date] = result.GetValueOrDefault(date) + 1;
        }
        return result;
    }

    public string? GetSetting(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO Settings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public void Backup(string destination)
    {
        var temporary = destination + ".tmp";
        try
        {
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                backup.Open();
                _connection.BackupDatabase(backup);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() => _connection.Dispose();
    private static string DateKey(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
