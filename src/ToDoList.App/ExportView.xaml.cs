using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToDoList.Core.Export;
using ToDoList.Core.Storage;
using Windows.Storage.Pickers;

namespace ToDoList.App;

public sealed partial class ExportView : UserControl
{
    private PlanRepository? _repository;
    private AppPaths? _paths;
    private Window? _owner;
    private DateOnly _anchor;
    private bool _updatingSelection;
    private bool _settingName;
    private bool _autoName = true;
    private string _folder = "";
    private IReadOnlyDictionary<DateOnly, int> _counts = new Dictionary<DateOnly, int>();

    public event EventHandler? BackRequested;
    public bool IsExporting { get; private set; }

    public ExportView() => InitializeComponent();

    public void Open(PlanRepository repository, AppPaths paths, DateOnly selected, Window owner)
    {
        _repository = repository;
        _paths = paths;
        _owner = owner;
        _anchor = selected;
        _folder = paths.ExportDirectory;
        FolderPath.Text = "Exports（程序文件夹内）";
        ExportNotice.IsOpen = false;
        ResultButtons.Visibility = Visibility.Collapsed;
        ResultButtons.Children.Clear();
        ExportStatus.Text = "计划已保存，可以开始导出。";
        _autoName = true;
        _counts = repository.GetDayCounts(DateOnly.MinValue, DateOnly.MaxValue);
        // Boundary instants use UTC to remain representable in all time zones.
        var minimum = new DateTimeOffset(1, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var maximum = new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero);
        ExportCalendar.MinDate = minimum;
        ExportCalendar.MaxDate = maximum;
        RangeStart.MinDate = RangeEnd.MinDate = minimum;
        RangeStart.MaxDate = RangeEnd.MaxDate = maximum;
        SelectWeek();
        ExportCalendar.SetDisplayDate(ToCalendarDate(selected));
    }

    private static DateTimeOffset ToCalendarDate(DateOnly date)
    {
        var local = date.ToDateTime(new TimeOnly(12, 0));
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        var utcTicks = local.Ticks - offset.Ticks;
        return utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks
            ? new DateTimeOffset(local, TimeSpan.Zero) : new DateTimeOffset(local, offset);
    }
    private DateOnly[] Selection() => ExportCalendar.SelectedDates.Select(date => DateOnly.FromDateTime(date.DateTime)).Distinct().Order().ToArray();

    private void SelectDates(IEnumerable<DateOnly> dates)
    {
        _updatingSelection = true;
        try
        {
            ExportCalendar.SelectedDates.Clear();
            foreach (var date in dates) ExportCalendar.SelectedDates.Add(ToCalendarDate(date));
        }
        finally { _updatingSelection = false; }
        RefreshSelection();
    }

    private void SelectWeek()
    {
        var dates = ExportPlanner.WeekContaining(_anchor);
        RangeStart.Date = ToCalendarDate(dates[0]);
        RangeEnd.Date = ToCalendarDate(dates[^1]);
        SelectDates(dates);
    }

    private void RefreshSelection()
    {
        var dates = Selection();
        SelectionSummary.Text = dates.Length == 0 ? "尚未选择日期" : $"已选择 {dates.Length} 天 · {dates[0]:yyyy.MM.dd}—{dates[^1]:yyyy.MM.dd}";
        if (_autoName)
        {
            _settingName = true;
            FileNameInput.Text = ExportPlanner.TitleFor(dates);
            _settingName = false;
        }
    }

    private void Dates_Changed(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (!_updatingSelection && FileNameInput is not null) RefreshSelection();
    }

    private void DayItem_Changing(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
    {
        var date = DateOnly.FromDateTime(args.Item.Date.DateTime);
        args.Item.SetDensityColors(_counts.ContainsKey(date) ? new[] { Colors.Teal } : Array.Empty<Windows.UI.Color>());
    }

    private void FileName_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_settingName) _autoName = false;
    }

    private void ApplyRange_Click(object sender, RoutedEventArgs e)
    {
        if (RangeStart.Date is null || RangeEnd.Date is null) { Notice("请选择完整的起止日期。"); return; }
        var start = DateOnly.FromDateTime(RangeStart.Date.Value.DateTime);
        var end = DateOnly.FromDateTime(RangeEnd.Date.Value.DateTime);
        if (start > end) { Notice("结束日期不能早于开始日期。"); return; }
        if (end.DayNumber - start.DayNumber > 3660) { Notice("一次连续范围最多选择 3661 天，请分批导出。"); return; }
        SelectDates(Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays));
        ExportCalendar.SetDisplayDate(ToCalendarDate(start));
        ExportNotice.IsOpen = false;
    }

    private void SelectWeek_Click(object sender, RoutedEventArgs e) => SelectWeek();
    private void ClearDates_Click(object sender, RoutedEventArgs e) => SelectDates([]);
    private void Back_Click(object sender, RoutedEventArgs e) { if (!IsExporting) BackRequested?.Invoke(this, EventArgs.Empty); }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner!));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            _folder = folder.Path;
            var relative = Path.GetRelativePath(_paths!.Root, _folder);
            FolderPath.Text = relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative)
                ? relative + "（程序文件夹内）" : _folder;
        }
        catch (Exception exception) { Notice("未能选择文件夹：" + exception.Message); }
    }

    private async void ConfirmExport_Click(object sender, RoutedEventArgs e)
    {
        if (IsExporting || _repository is null || _paths is null) return;
        IsExporting = true;
        ExportForm.IsEnabled = BackButton.IsEnabled = ConfirmExportButton.IsEnabled = false;
        ExportNotice.IsOpen = false;
        ResultButtons.Visibility = Visibility.Collapsed;
        try
        {
            var formats = (WordFormat.IsChecked == true ? ExportFormats.Word : 0) | (PdfFormat.IsChecked == true ? ExportFormats.Pdf : 0);
            var name = ExportPlanner.ValidateFileName(FileNameInput.Text);
            var targets = ExportService.TargetFiles(_folder, name, formats);
            // Snapshot database data on the UI thread. Rendering is isolated from
            // mutable editor models and never shares the SQLite connection.
            var plan = ExportPlanner.Create(Selection(), _repository.LoadDay, IncludeEmpty.IsChecked == true);
            var existing = targets.Where(File.Exists).Select(Path.GetFileName).ToArray();
            var overwrite = false;
            if (existing.Length > 0)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = "覆盖已有文件？",
                    Content = string.Join("\n", existing) + "\n\n这些文件已存在，确认后将替换。",
                    PrimaryButtonText = "确认覆盖", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
                };
                if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary) return;
                overwrite = true;
            }
            ExportProgress.Visibility = Visibility.Visible;
            ExportProgress.IsActive = true;
            ExportStatus.Text = "正在生成文档，请稍候…";
            var font = _paths.FontFile;
            var destination = _folder;
            var files = await Task.Run(() => new ExportService(new DocumentExporter(font)).Export(plan, destination, name, formats, overwrite));
            ExportNotice.Severity = InfoBarSeverity.Success;
            ExportNotice.Title = "导出完成";
            ExportNotice.Message = $"已导出 {plan.Days.Count} 天的计划。";
            ExportNotice.IsOpen = true;
            ExportStatus.Text = "文件已保存，可以打开或继续选择日期。";
            ResultButtons.Children.Clear();
            foreach (var file in files)
            {
                var button = new Button { Content = Path.GetExtension(file) == ".pdf" ? "打开 PDF" : "打开 Word", Tag = file };
                button.Click += OpenResult_Click;
                ResultButtons.Children.Add(button);
            }
            var folderButton = new Button { Content = "打开所在文件夹", Tag = destination };
            folderButton.Click += OpenResult_Click;
            ResultButtons.Children.Add(folderButton);
            ResultButtons.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            ExportStatus.Text = "导出未完成，计划数据仍已保存。";
            Notice(exception.Message);
        }
        finally
        {
            IsExporting = false;
            ExportForm.IsEnabled = BackButton.IsEnabled = ConfirmExportButton.IsEnabled = true;
            ExportProgress.IsActive = false;
            ExportProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo((string)((Button)sender).Tag) { UseShellExecute = true }); }
        catch (Exception exception) { Notice("无法打开，请在保存位置手动查看：" + exception.Message); }
    }

    private void Notice(string message)
    {
        ExportNotice.Severity = InfoBarSeverity.Error;
        ExportNotice.Title = "请检查导出设置";
        ExportNotice.Message = message;
        ExportNotice.IsOpen = true;
    }
}
