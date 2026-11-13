using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Pdf.IO;
using ToDoList.Core.Export;
using ToDoList.Core.Models;
using ToDoList.Core.Storage;

var output = args.Length == 2 && args[0] == "--output"
    ? Path.GetFullPath(args[1])
    : Path.Combine(Path.GetTempPath(), "ToDoList-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    assertions++;
    Console.WriteLine("PASS: " + message);
}
void Reject<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, message); return; }
    throw new InvalidOperationException("FAIL: expected " + typeof(T).Name + ": " + message);
}

try
{
    var day = new DateOnly(2026, 11, 16);
    var other = day.AddDays(2);
    var blank = day.AddDays(3);
    var paths = new AppPaths(Path.Combine(output, "portable-source"));
    paths.EnsureWritable();
    Check(paths.DatabaseFile.StartsWith(paths.Root, StringComparison.OrdinalIgnoreCase), "database is inside the portable root");
    var first = new PlanItem { Content = "第一项：阅读并整理笔记\n保留用户的换行。", Duration = "30分钟" };
    var second = new PlanItem { Content = "第二项：完成项目方案", Duration = "约2小时" };
    using (var repository = new PlanRepository(paths.DatabaseFile))
    {
        repository.SaveDay(day, [first, second, new PlanItem { Content = "\n\u3000\t" }]);
        repository.SaveDay(other, [new PlanItem { Content = "隔日计划，不应导出中间日期", Duration = "待定" }]);
        repository.SaveDay(blank, [new PlanItem()]);
        var items = repository.LoadDay(day);
        repository.SaveDay(day, [items[1], items[0], items[2]]);
        repository.SetSetting("CalendarFraction", "0.37");
        repository.Backup(paths.BackupFile);
        Check(repository.GetDayCounts(day, blank)[day] == 2, "blank editor rows are excluded from calendar counts");
        Check(repository.LoadDay(other).Single().Content.Contains("隔日计划"), "saving one day preserves other dates");
    }

    // Copying the closed data directory models moving the complete application.
    var moved = new AppPaths(Path.Combine(output, "portable-moved"));
    moved.EnsureWritable();
    File.Copy(paths.DatabaseFile, moved.DatabaseFile);
    using var reopened = new PlanRepository(moved.DatabaseFile);
    var reloaded = reopened.LoadDay(day);
    Check(reloaded[0].Id == second.Id && reloaded[1].Id == first.Id, "reordered IDs survive reopen and a directory move");
    Check(reloaded.Select(item => item.Number).SequenceEqual([1, 2, 3]), "display numbers are consecutive after reload");
    Check(reloaded[1].Content == first.Content && reloaded[0].Duration == second.Duration, "manual content, duration and newlines survive reopen");
    Check(reopened.GetSetting("CalendarFraction") == "0.37", "splitter layout survives reopen");
    using (var backup = new PlanRepository(paths.BackupFile))
        Check(backup.LoadDay(day)[0].Id == second.Id, "SQLite backup can be opened independently");

    // A duplicate primary key fails mid-transaction. The old day must remain.
    Reject<Microsoft.Data.Sqlite.SqliteException>(() => reopened.SaveDay(day, [first, first]), "failed save rolls back");
    Check(reopened.LoadDay(day)[0].Id == second.Id && reopened.LoadDay(day).Count == 3, "failed save retains the previous complete plan");
    var plan = ExportPlanner.Create([other, day, other, blank], reopened.LoadDay, false);
    Check(plan.Days.Select(d => d.Date).SequenceEqual([day, other]), "nonconsecutive selection sorts, deduplicates and skips blank dates");
    Check(plan.Days[0].Items[0].Id == second.Id, "export uses the saved row order");
    Check(plan.Title.Contains("选定3天"), "nonconsecutive title describes all selected dates");
    var includeBlank = ExportPlanner.Create([blank], reopened.LoadDay, true);
    Check(includeBlank.Days.Count == 1 && includeBlank.Days[0].Items.Count == 0, "blank date can be included for handwriting");
    Reject<InvalidOperationException>(() => ExportPlanner.Create([], reopened.LoadDay, false), "empty date selection is rejected");
    Reject<InvalidOperationException>(() => ExportPlanner.Create([blank], reopened.LoadDay, false), "empty export is rejected");
    reopened.SaveDay(blank, [new PlanItem { Duration = "仅有预计用时" }]);
    Reject<InvalidOperationException>(() => ExportPlanner.Create([blank], reopened.LoadDay, false), "duration-only row is rejected");
    Check(ExportPlanner.WeekContaining(day.AddDays(6))[0] == day, "week starts on Monday, including Sunday selection");
    Check(ExportPlanner.WeekContaining(DateOnly.MaxValue).Last() == DateOnly.MaxValue, "last calendar week stays inside DateOnly limits");
    Check(ExportPlanner.TitleFor([day]) == "2026.11.16 to-do-list", "single-day title is compact");
    Check(!ExportPlanner.TitleFor([day, day.AddDays(1)]).Contains("选定"), "continuous selection has a simple range title");
    foreach (var invalid in new[] { "../escape", "bad:name", "CON", "NUL.txt", "COM1", "LPT¹", "ends.", "", new string('x', 121) })
        Reject<InvalidOperationException>(() => ExportPlanner.ValidateFileName(invalid), "invalid filename rejected: " + invalid);

    var font = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "LXGWNeoXiHei.ttf");
    Check(File.Exists(font), "portable Chinese font is copied to the output");
    var exporter = new ExportService(new DocumentExporter(font));
    var files = exporter.Export(plan, Path.Combine(output, "documents"), "不连续日期计划", ExportFormats.Word | ExportFormats.Pdf, false);
    var word = files.Single(f => f.EndsWith(".docx"));
    var pdf = files.Single(f => f.EndsWith(".pdf"));
    using (var document = WordprocessingDocument.Open(word, false))
    {
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        Check(errors.Length == 0, "DOCX passes the Open XML schema validator: " + string.Join("; ", errors.Take(5).Select(e => e.Description)));
        var text = string.Concat(document.MainDocumentPart!.Document.Descendants<Text>().Select(t => t.Text));
        Check(text.Contains(second.Content) && text.Contains("第一项") && text.IndexOf(second.Content, StringComparison.Ordinal) < text.IndexOf("第一项", StringComparison.Ordinal), "DOCX preserves drag order");
        Check(text.Contains(ExportPlanner.DateHeading(day)) && text.Contains(ExportPlanner.DateHeading(other)) && !text.Contains(ExportPlanner.DateHeading(day.AddDays(1))), "DOCX contains only selected dates");
        Check(text.Count(c => c == '□') == 3, "DOCX includes one printable box per exported plan");
        Check(document.MainDocumentPart.FontTablePart!.FontParts.Any(), "DOCX embeds the portable font");
        var title = document.MainDocumentPart.Document.Body!.Elements<Paragraph>().First();
        Check(title.Descendants<FontSize>().Any(s => s.Val?.Value == "52"), "title uses 26pt text");
    }
    Check(Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5) == "%PDF-", "PDF signature is valid");
    using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import))
        Check(document.PageCount > 0 && Math.Abs(document.Pages[0].Width.Point - 595.28) < 2, "PDF has readable A4 pages");
    var before = File.ReadAllBytes(word);
    Reject<IOException>(() => exporter.Export(plan, Path.GetDirectoryName(word)!, "不连续日期计划", ExportFormats.Word, false), "overwrite requires confirmation");
    Check(before.SequenceEqual(File.ReadAllBytes(word)), "rejected overwrite preserves original bytes");

    // Force the second replacement to fail after the first has been installed.
    var originalPdf = File.ReadAllBytes(pdf);
    using (var locked = new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.None))
        Reject<IOException>(() => exporter.Export(includeBlank, Path.GetDirectoryName(word)!, "不连续日期计划", ExportFormats.Word | ExportFormats.Pdf, true), "locked second file aborts both replacements");
    Check(before.SequenceEqual(File.ReadAllBytes(word)) && originalPdf.SequenceEqual(File.ReadAllBytes(pdf)), "failed pair export restores both original files");

    var longPlan = new ExportPlan("长内容分页检查 to-do-list", [new DayPlan(day,
        [new PlanItem { Content = string.Join("\n", Enumerable.Repeat("中文长计划：阅读、复盘、继续编辑，不丢失后续文字。", 250)) + "分页末尾标记", Duration = "自由输入预计用时" }])]);
    var longFiles = exporter.Export(longPlan, Path.Combine(output, "documents"), "长内容", ExportFormats.Word | ExportFormats.Pdf, false);
    using (var document = PdfReader.Open(longFiles.Single(f => f.EndsWith(".pdf")), PdfDocumentOpenMode.Import))
        Check(document.PageCount > 1, "very long Chinese plan produces multiple PDF pages");
    using (var document = WordprocessingDocument.Open(longFiles.Single(f => f.EndsWith(".docx")), false))
        Check(string.Concat(document.MainDocumentPart!.Document.Descendants<Text>().Select(t => t.Text)).Contains("分页末尾标记"), "long DOCX retains the final text");
    Check(!Directory.EnumerateDirectories(Path.Combine(output, "documents"), ".todo-export-*").Any(), "successful exports and rollbacks clean their staging directories");
    Console.WriteLine($"\nPASS: {assertions} assertions. Inspect documents at: {output}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.Error.WriteLine("Artifacts retained at: " + output);
    return 1;
}
