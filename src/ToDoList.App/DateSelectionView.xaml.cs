using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToDoList.Core.Export;
using ToDoList.Core.Models;
using Windows.UI;

namespace ToDoList.App;

public sealed partial class DateSelectionView : UserControl
{
    private static readonly Color[] PlanDensity = [Microsoft.UI.Colors.Teal];
    private DateOnly _anchor;
    private bool _updating;
    private bool _isBatchAdd;
    private IReadOnlyDictionary<DateOnly, int> _counts = new Dictionary<DateOnly, int>();

    public DateSelectionView()
    {
        InitializeComponent();
        var minimum = new DateTimeOffset(1, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var maximum = new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero);
        Calendar.MinDate = RangeStart.MinDate = RangeEnd.MinDate = minimum;
        Calendar.MaxDate = RangeStart.MaxDate = RangeEnd.MaxDate = maximum;
    }

    public event EventHandler? SelectionChanged;

    public DateOnly[] SelectedDates => Calendar.SelectedDates.Select(date => DateOnly.FromDateTime(date.DateTime))
        .Distinct().Order().ToArray();

    public void Open(DateOnly anchor, IReadOnlyDictionary<DateOnly, int> counts, bool isBatchAdd = false)
    {
        _anchor = anchor;
        _counts = counts;
        if (isBatchAdd != _isBatchAdd)
        {
            if (isBatchAdd)
            {
                DateFields.Children.Remove(RangeFields);
                RangeFlyout.Content = RangeFields;
            }
            else
            {
                RangeFlyout.Content = null;
                DateFields.Children.Insert(2, RangeFields);
            }
            _isBatchAdd = isBatchAdd;
        }
        RangeButton.Visibility = CancelRangeButton.Visibility = isBatchAdd ? Visibility.Visible : Visibility.Collapsed;
        ApplyRangeButton.Content = isBatchAdd ? "加入所选日期" : "应用范围";
        if (isBatchAdd)
        {
            RangeStart.Date = RangeEnd.Date = null;
            SelectDates([]);
        }
        else
        {
            var week = ExportPlanner.WeekContaining(anchor);
            RangeStart.Date = ToCalendarDate(week[0]);
            RangeEnd.Date = ToCalendarDate(week[^1]);
            SelectDates(week);
        }
        Calendar.SetDisplayDate(ToCalendarDate(anchor));
        RefreshVisibleDays(Calendar);
    }

    private static DateTimeOffset ToCalendarDate(DateOnly date)
    {
        var local = date.ToDateTime(new TimeOnly(12, 0));
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        var utcTicks = local.Ticks - offset.Ticks;
        return utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks
            ? new DateTimeOffset(local, TimeSpan.Zero) : new DateTimeOffset(local, offset);
    }

    private void SelectDates(IEnumerable<DateOnly> dates)
    {
        _updating = true;
        try
        {
            Calendar.SelectedDates.Clear();
            foreach (var date in dates) Calendar.SelectedDates.Add(ToCalendarDate(date));
        }
        finally { _updating = false; }
        Refresh();
    }

    private void Refresh()
    {
        RangeError.IsOpen = false;
        SelectionSummary.Text = DateRanges.SelectionSummary(SelectedDates);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Dates_Changed(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (!_updating && SelectionSummary is not null) Refresh();
    }

    private void DayItem_Changing(CalendarView sender, CalendarViewDayItemChangingEventArgs args) => SetDensity(args.Item);

    private void RefreshVisibleDays(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CalendarViewDayItem day) SetDensity(day);
            else RefreshVisibleDays(child);
        }
    }

    private void SetDensity(CalendarViewDayItem day)
    {
        var date = DateOnly.FromDateTime(day.Date.DateTime);
        day.SetDensityColors(_counts.ContainsKey(date) ? PlanDensity : Array.Empty<Color>());
    }

    private void ApplyRange_Click(object sender, RoutedEventArgs e)
    {
        if (RangeStart.Date is not { } first || RangeEnd.Date is not { } last)
        {
            ShowError("请选择完整的起止日期。");
            return;
        }
        var start = DateOnly.FromDateTime(first.DateTime);
        var end = DateOnly.FromDateTime(last.DateTime);
        if (start > end) { ShowError("结束日期不能早于开始日期。"); return; }
        if (end.DayNumber - start.DayNumber > 3660) { ShowError("一次连续范围最多选择 3661 天。"); return; }
        var range = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays);
        SelectDates(_isBatchAdd ? SelectedDates.Union(range) : range);
        Calendar.SetDisplayDate(ToCalendarDate(start));
        if (_isBatchAdd) RangeFlyout.Hide();
    }

    private void ShowError(string message)
    {
        RangeError.Message = message;
        RangeError.IsOpen = true;
    }

    private void SelectWeek_Click(object sender, RoutedEventArgs e)
    {
        var dates = ExportPlanner.WeekContaining(_anchor);
        RangeStart.Date = ToCalendarDate(dates[0]);
        RangeEnd.Date = ToCalendarDate(dates[^1]);
        SelectDates(dates);
        Calendar.SetDisplayDate(ToCalendarDate(_anchor));
    }

    private void ClearDates_Click(object sender, RoutedEventArgs e) => SelectDates([]);

    private void CancelRange_Click(object sender, RoutedEventArgs e)
    {
        RangeError.IsOpen = false;
        RangeFlyout.Hide();
    }
}
