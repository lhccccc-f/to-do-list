using System.Globalization;

namespace ToDoList.Core.Models;

public static class DateRanges
{
    public static string Format(IEnumerable<DateOnly> selection)
    {
        var dates = selection.Distinct().Order().ToArray();
        var ranges = new List<string>();
        int? lastYear = null;
        for (var first = 0; first < dates.Length;)
        {
            var last = first;
            while (last + 1 < dates.Length && dates[last + 1].DayNumber - dates[last].DayNumber == 1)
                last++;
            var range = FormatDate(dates[first]);
            if (last != first) range += "-" + FormatDate(dates[last]);
            ranges.Add(range);
            first = last + 1;
        }
        return string.Join(" ，", ranges);

        string FormatDate(DateOnly date)
        {
            var format = date.Year == lastYear ? "MM.dd" : "yyyy.MM.dd";
            lastYear = date.Year;
            return date.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    public static string SelectionSummary(IReadOnlyCollection<DateOnly> dates) =>
        dates.Count == 0 ? "尚未选择日期" : $"已选择{dates.Count}天 · {Format(dates)}";
}
