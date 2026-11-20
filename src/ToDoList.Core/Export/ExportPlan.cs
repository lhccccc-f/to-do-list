using System.Globalization;
using ToDoList.Core.Models;

namespace ToDoList.Core.Export;

[Flags]
public enum ExportFormats { Word = 1, Pdf = 2, Jpg = 4 }

public sealed record ExportPlan(string Title, IReadOnlyList<DayPlan> Days, bool ShowCheckboxes = true);

public static class ExportPlanner
{
    public const string FontFamily = "LXGW Neo XiHei";
    public const int MaxFileNameLength = 240;
    public static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

    public static IReadOnlyList<DateOnly> WeekContaining(DateOnly date)
    {
        var start = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return Enumerable.Range(0, Math.Min(7, DateOnly.MaxValue.DayNumber - start.DayNumber + 1)).Select(start.AddDays).ToArray();
    }

    public static string TitleFor(IEnumerable<DateOnly> selection)
    {
        var dates = DateRanges.Format(selection);
        return dates.Length == 0 ? "to-do-list" : dates + "  to-do-list";
    }

    public static ExportPlan Create(IEnumerable<DateOnly> selection, Func<DateOnly, IReadOnlyList<PlanItem>> load,
        bool includeEmpty, bool showCheckboxes = true)
    {
        var dates = selection.Distinct().Order().ToArray();
        if (dates.Length == 0) throw new InvalidOperationException("请至少选择一个导出日期。");
        var days = new List<DayPlan>();
        foreach (var date in dates)
        {
            var entries = load(date).Where(item => !item.IsBlank).Select(item => item.Snapshot()).ToArray();
            if (entries.Any(item => string.IsNullOrWhiteSpace(item.Content)))
                throw new InvalidOperationException($"{date:yyyy年MM月dd日} 有只填写预计用时的行，请先补充计划内容。");
            if (entries.Length > 0 || includeEmpty) days.Add(new DayPlan(date, entries));
        }
        if (days.Count == 0) throw new InvalidOperationException("所选日期没有可导出的计划。可以开启“包含空白日期”。");
        return new ExportPlan(TitleFor(dates), days, showCheckboxes);
    }

    public static string ValidateFileName(string name)
    {
        name = name.Trim();
        if (name.Length > MaxFileNameLength)
            throw new InvalidOperationException($"文件名超过 {MaxFileNameLength} 个字符，请用编辑按钮缩短名称。");
        if (name.Length == 0 || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            throw new InvalidOperationException("请填写有效的文件名。文件名不能包含路径或 Windows 保留字符，也不能以句点结尾。");
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
            throw new InvalidOperationException("此文件名是 Windows 保留名称，请换一个名称。");
        return name;
    }

    public static string DateHeading(DateOnly date) => date.ToString("yyyy年MM月dd日 dddd", Chinese) + "：";
}
