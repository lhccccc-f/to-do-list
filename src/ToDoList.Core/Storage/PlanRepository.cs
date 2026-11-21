using System.Globalization;
using Microsoft.Data.Sqlite;
using ToDoList.Core.Models;

namespace ToDoList.Core.Storage;

public sealed class PlanRepository : IDisposable
{
    public const int FirstBookId = 1;
    private readonly SqliteConnection _connection;

    public PlanRepository(string databaseFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databaseFile),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
        try
        {
            _connection.Open();
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (version > 2) throw new InvalidOperationException("此计划数据库由更新版本创建，请使用更新版本的程序打开。");

            command.CommandText = "PRAGMA journal_mode = DELETE; PRAGMA synchronous = FULL; PRAGMA busy_timeout = 5000;";
            command.ExecuteNonQuery();
            if (version < 2)
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Plans'";
                if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    Backup(Path.ChangeExtension(databaseFile, ".before-calendar-books.db"));
                MigrateCalendarBooks();
            }
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    private void MigrateCalendarBooks()
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Plans (
                Id TEXT NOT NULL PRIMARY KEY,
                PlanDate TEXT NOT NULL,
                Position INTEGER NOT NULL,
                Content TEXT NOT NULL,
                Duration TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Settings (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL);
            CREATE TABLE CalendarBooks (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                SelectedDate TEXT
            );
            INSERT INTO CalendarBooks(Id, Name, SelectedDate)
            VALUES (1, '日历簿 1', (SELECT Value FROM Settings WHERE Key = 'SelectedDate'));
            CREATE TABLE PlanBatches (
                Id INTEGER PRIMARY KEY,
                BookId INTEGER NOT NULL REFERENCES CalendarBooks(Id),
                Content TEXT NOT NULL,
                DateSummary TEXT NOT NULL
            );
            ALTER TABLE Plans ADD COLUMN BookId INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE Plans ADD COLUMN BatchId INTEGER REFERENCES PlanBatches(Id);
            DROP INDEX IF EXISTS IX_Plans_Date_Position;
            CREATE INDEX IX_Plans_Book_Date_Position ON Plans(BookId, PlanDate, Position);
            CREATE INDEX IX_Plans_Batch ON Plans(BatchId) WHERE BatchId IS NOT NULL;
            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<PlanItem> LoadDay(DateOnly date) => LoadDay(date, FirstBookId);

    public IReadOnlyList<PlanItem> LoadDay(DateOnly date, int bookId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, Content, Duration FROM Plans WHERE BookId = $book AND PlanDate = $date ORDER BY Position, Id";
        command.Parameters.AddWithValue("$book", bookId);
        command.Parameters.AddWithValue("$date", DateKey(date));
        using var reader = command.ExecuteReader();
        var items = new List<PlanItem>();
        while (reader.Read())
            items.Add(new PlanItem { Id = reader.GetString(0), Content = reader.GetString(1),
                Duration = reader.GetString(2), Number = items.Count + 1 });
        return items;
    }

    // Delete + insert is a single transaction: a failed save preserves the previous day.
    public void SaveDay(DateOnly date, IEnumerable<PlanItem> items, int bookId = FirstBookId)
    {
        var snapshot = items.Select(item => item.Snapshot()).ToArray();
        using var transaction = _connection.BeginTransaction();
        var originalBatchRows = new Dictionary<string, (int BatchId, string Content, string Duration)>();
        using (var original = _connection.CreateCommand())
        {
            original.Transaction = transaction;
            original.CommandText = """
                SELECT Id, BatchId, Content, Duration FROM Plans
                WHERE BookId = $book AND PlanDate = $date AND BatchId IS NOT NULL
                """;
            original.Parameters.AddWithValue("$book", bookId);
            original.Parameters.AddWithValue("$date", DateKey(date));
            using var reader = original.ExecuteReader();
            while (reader.Read())
                originalBatchRows.Add(reader.GetString(0), (reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }
        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Plans WHERE BookId = $book AND PlanDate = $date";
            delete.Parameters.AddWithValue("$book", bookId);
            delete.Parameters.AddWithValue("$date", DateKey(date));
            delete.ExecuteNonQuery();
        }
        for (var i = 0; i < snapshot.Length; i++)
        {
            var item = snapshot[i];
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Plans(Id, BookId, PlanDate, Position, Content, Duration, BatchId)
                VALUES($id, $book, $date, $position, $content, $duration, $batch)
                """;
            insert.Parameters.AddWithValue("$id", item.Id);
            insert.Parameters.AddWithValue("$book", bookId);
            insert.Parameters.AddWithValue("$date", DateKey(date));
            insert.Parameters.AddWithValue("$position", i);
            insert.Parameters.AddWithValue("$content", item.Content);
            insert.Parameters.AddWithValue("$duration", item.Duration);
            var unchanged = originalBatchRows.TryGetValue(item.Id, out var original)
                && original.Content == item.Content && original.Duration == item.Duration;
            insert.Parameters.AddWithValue("$batch", unchanged ? (object)original.BatchId : DBNull.Value);
            insert.ExecuteNonQuery();
        }
        PruneBatches(bookId, transaction);
        transaction.Commit();
    }

    public IReadOnlyDictionary<DateOnly, int> GetDayCounts(DateOnly from, DateOnly to, int bookId = FirstBookId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT PlanDate, Content, Duration FROM Plans
            WHERE BookId = $book AND PlanDate >= $from AND PlanDate <= $to
            """;
        command.Parameters.AddWithValue("$book", bookId);
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

    public IReadOnlyList<CalendarBook> LoadBooks() => LoadBooks(null);

    private List<CalendarBook> LoadBooks(SqliteTransaction? transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Name, SelectedDate FROM CalendarBooks ORDER BY Id";
        using var reader = command.ExecuteReader();
        var books = new List<CalendarBook>();
        while (reader.Read())
        {
            DateOnly? selected = !reader.IsDBNull(2) && DateOnly.TryParseExact(reader.GetString(2),
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
            books.Add(new CalendarBook(reader.GetInt32(0), reader.GetString(1), selected));
        }
        return books;
    }

    public CalendarBook CreateBook()
    {
        var books = LoadBooks();
        var number = books.Count + 1;
        while (books.Any(book => book.Name == $"日历簿 {number}")) number++;
        var name = $"日历簿 {number}";
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO CalendarBooks(Name) VALUES($name) RETURNING Id";
        command.Parameters.AddWithValue("$name", name);
        return new CalendarBook(Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture), name, null);
    }

    public void RenameBook(int bookId, string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 60) throw new InvalidOperationException("日历簿名称须为 1–60 个字符。");
        if (LoadBooks().Any(book => book.Id != bookId && string.Equals(book.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已有同名日历簿，请换个名字。");
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE CalendarBooks SET Name = $name WHERE Id = $book";
        command.Parameters.AddWithValue("$book", bookId);
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    public CalendarBook DeleteBook(int bookId, int activeBookId)
    {
        using var transaction = _connection.BeginTransaction();
        var books = LoadBooks(transaction);
        var index = books.FindIndex(book => book.Id == bookId);
        if (index < 0) throw new InvalidOperationException("此日历簿已不存在。");
        books.RemoveAt(index);

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM Plans WHERE BookId = $book;
            DELETE FROM PlanBatches WHERE BookId = $book;
            DELETE FROM CalendarBooks WHERE Id = $book;
            """;
        command.Parameters.AddWithValue("$book", bookId);
        command.ExecuteNonQuery();

        if (books.Count == 0)
        {
            command.CommandText = "INSERT INTO CalendarBooks(Id, Name) VALUES (1, '日历簿 1')";
            command.ExecuteNonQuery();
            books.Add(new CalendarBook(FirstBookId, "日历簿 1", null));
        }

        // Keep the active book if it survived. Otherwise use the next book,
        // or the previous one when the deleted book was last in the list.
        var active = books.FirstOrDefault(book => book.Id == activeBookId)
            ?? books[Math.Min(index, books.Count - 1)];
        SetSetting("ActiveBook", active.Id.ToString(CultureInfo.InvariantCulture), transaction);
        transaction.Commit();
        return active;
    }

    public void SetBookDate(int bookId, DateOnly date)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE CalendarBooks SET SelectedDate = $date WHERE Id = $book";
        command.Parameters.AddWithValue("$book", bookId);
        command.Parameters.AddWithValue("$date", DateKey(date));
        command.ExecuteNonQuery();
    }

    public void AddBatch(int bookId, IEnumerable<DateOnly> dates, string content, string duration)
    {
        var selection = dates.Distinct().Order().ToArray();
        if (selection.Length == 0 || string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("请填写计划内容并选择日期。");
        using var transaction = _connection.BeginTransaction();
        using var batch = _connection.CreateCommand();
        batch.Transaction = transaction;
        batch.CommandText = """
            INSERT INTO PlanBatches(BookId, Content, DateSummary)
            VALUES ($book, $content, $summary) RETURNING Id
            """;
        batch.Parameters.AddWithValue("$book", bookId);
        batch.Parameters.AddWithValue("$content", content);
        batch.Parameters.AddWithValue("$summary", DateRanges.Format(selection));
        var batchId = Convert.ToInt32(batch.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Plans(Id, BookId, PlanDate, Position, Content, Duration, BatchId)
            SELECT $id, $book, $date, COALESCE(MAX(Position) + 1, 0), $content, $duration, $batch
            FROM Plans WHERE BookId = $book AND PlanDate = $date
            """;
        insert.Parameters.AddWithValue("$book", bookId);
        insert.Parameters.AddWithValue("$content", content);
        insert.Parameters.AddWithValue("$duration", duration);
        insert.Parameters.AddWithValue("$batch", batchId);
        var id = insert.Parameters.Add("$id", SqliteType.Text);
        var date = insert.Parameters.Add("$date", SqliteType.Text);
        foreach (var day in selection)
        {
            id.Value = Guid.NewGuid().ToString("N");
            date.Value = DateKey(day);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<PlanBatch> LoadBatches(int bookId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT b.Id, b.Content, p.PlanDate, COUNT(*)
            FROM PlanBatches b JOIN Plans p ON p.BatchId = b.Id AND p.BookId = b.BookId
            WHERE b.BookId = $book
            GROUP BY b.Id, p.PlanDate
            ORDER BY b.Id DESC, p.PlanDate
            """;
        command.Parameters.AddWithValue("$book", bookId);
        using var reader = command.ExecuteReader();
        var days = new List<(int Id, string Content, DateOnly Date, int Count)>();
        while (reader.Read())
        {
            var date = DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            days.Add((reader.GetInt32(0), reader.GetString(1), date, reader.GetInt32(3)));
        }
        return days.GroupBy(day => day.Id).Select(batch =>
            new PlanBatch(batch.Key, batch.First().Content, DateRanges.Format(batch.Select(day => day.Date)),
                batch.Sum(day => day.Count), batch.Count())).ToArray();
    }

    public void DeleteBatch(int bookId, int batchId)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Plans WHERE BookId = $book AND BatchId = $batch";
        command.Parameters.AddWithValue("$book", bookId);
        command.Parameters.AddWithValue("$batch", batchId);
        command.ExecuteNonQuery();
        PruneBatches(bookId, transaction);
        transaction.Commit();
    }

    private void PruneBatches(int bookId, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM PlanBatches WHERE BookId = $book
            AND NOT EXISTS (SELECT 1 FROM Plans WHERE BatchId = PlanBatches.Id)
            """;
        command.Parameters.AddWithValue("$book", bookId);
        command.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value) => SetSetting(key, value, null);

    private void SetSetting(string key, string value, SqliteTransaction? transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
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
