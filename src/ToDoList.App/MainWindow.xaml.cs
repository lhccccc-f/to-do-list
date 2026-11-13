using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ToDoList.Core.Export;
using ToDoList.Core.Models;
using ToDoList.Core.Storage;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace ToDoList.App;

public sealed partial class MainWindow : Window
{
    private const string DragKey = "ToDoList.PlanItem";
    private readonly ObservableCollection<PlanItem> _items = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly AppPaths _paths = new();
    private PlanRepository? _repository;
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _month;
    private bool _loading;
    private bool _dirty;
    private bool _ready;
    private bool _showingDialog;
    private bool _closed;
    private bool _backupUnavailable;
    private double _calendarFraction = 1 / 2.7;
    private WindowSizing? _sizing;
    private FileStream? _sessionLock;

    public MainWindow()
    {
        InitializeComponent();
        _month = new DateOnly(_selected.Year, _selected.Month, 1);
        SystemBackdrop = new MicaBackdrop();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        _sizing = new WindowSizing(this, 960, 640);
        var scale = WindowSizing.DpiScale(this);
        AppWindow.Resize(new SizeInt32(Math.Min((int)(1380 * scale), area.WorkArea.Width), Math.Min((int)(920 * scale), area.WorkArea.Height)));
        PlanRows.ItemsSource = _items;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); TrySave(); };
        ExportPane.BackRequested += (_, _) => { ExportPane.Visibility = Visibility.Collapsed; MainPane.Visibility = Visibility.Visible; };
        AppWindow.Closing += Window_Closing;
        Closed += (_, _) =>
        {
            _closed = true;
            _saveTimer.Stop();
            _repository?.Dispose();
            _sessionLock?.Dispose();
            _sizing?.Dispose();
        };
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        try
        {
            _paths.EnsureWritable();
            try { _sessionLock = new FileStream(Path.Combine(_paths.DataDirectory, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) { throw new IOException("此数据目录可能已被另一个程序窗口打开，请先关闭那个窗口。", exception); }
            _repository = new PlanRepository(_paths.DatabaseFile);
            if (double.TryParse(_repository.GetSetting("CalendarFraction"), NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction))
                _calendarFraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0.1, 0.9) : 1 / 2.7;
            CalendarColumn.Width = new GridLength(_calendarFraction, GridUnitType.Star);
            EditorColumn.Width = new GridLength(1 - _calendarFraction, GridUnitType.Star);
            if (DateOnly.TryParseExact(_repository.GetSetting("SelectedDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var savedDate))
                _selected = savedDate;
            _month = new DateOnly(_selected.Year, _selected.Month, 1);
            // Back up the last complete session before allowing any edits.
            try { _repository.Backup(_paths.BackupFile); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { _backupUnavailable = true; }
            _ready = true;
            LoadSelectedDay();
        }
        catch (Exception exception)
        {
            Workspace.IsHitTestVisible = false;
            DoneButton.IsEnabled = false;
            SaveStatus.Text = "数据未能打开，编辑已暂停。";
            await ShowMessage("无法打开计划", "请将整个程序文件夹放到可写目录（例如 D 盘的个人文件夹）后重新打开。\n\n" + exception.Message);
        }
    }

    private void LoadSelectedDay(IReadOnlyList<PlanItem>? loaded = null)
    {
        loaded ??= _repository!.LoadDay(_selected);
        _loading = true;
        try
        {
            foreach (var item in _items) item.PropertyChanged -= Item_Changed;
            _items.Clear();
            foreach (var item in loaded) AttachItem(item);
            if (_items.Count == 0) AttachItem(new PlanItem());
            Renumber();
            _dirty = false;
            SelectedDateLabel.Text = _selected.ToString("yyyy年M月d日", ExportPlanner.Chinese);
            SelectedWeekdayLabel.Text = _selected.ToString("dddd", ExportPlanner.Chinese) + (_selected == DateOnly.FromDateTime(DateTime.Today) ? " · 今天" : "");
            SaveStatus.Text = _backupUnavailable ? "计划已打开；本次自动备份暂不可用。" : "所有更改已保存到本机";
            RetrySaveButton.Visibility = Visibility.Collapsed;
            RenderCalendar();
        }
        finally { _loading = false; }
    }

    private void AttachItem(PlanItem item)
    {
        item.PropertyChanged += Item_Changed;
        _items.Add(item);
    }

    private void Item_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlanItem.Content) or nameof(PlanItem.Duration)) MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading || !_ready) return;
        _dirty = true;
        SaveStatus.Text = "正在保存…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private bool TrySave()
    {
        if (!_dirty) return true;
        try
        {
            _repository!.SaveDay(_selected, _items);
        }
        catch (Exception exception)
        {
            SaveStatus.Text = "保存失败，当前内容仍保留在窗口中：" + exception.Message;
            RetrySaveButton.Visibility = Visibility.Visible;
            return false;
        }
        _dirty = false;
        SaveStatus.Text = $"已保存 · {DateTime.Now:HH:mm:ss}";
        RetrySaveButton.Visibility = Visibility.Collapsed;
        try { RenderCalendar(); }
        catch (Exception exception) { SaveStatus.Text += "；日历刷新失败：" + exception.Message; }
        return true;
    }

    private async void SelectDate(DateOnly date)
    {
        if (!_ready) return;
        if (date == _selected)
        {
            _month = new DateOnly(date.Year, date.Month, 1);
            try { RenderCalendar(); }
            catch (Exception exception) { await ShowMessage("无法读取日历", exception.Message); }
            return;
        }
        if (!TrySave()) { await ShowMessage("暂时无法切换日期", "当前计划尚未保存成功。请关闭占用数据文件的程序、检查可写权限，再点击“重试保存”。"); return; }
        try
        {
            // Load before changing the selected date, so a read failure cannot
            // associate the previous day's editor contents with a different day.
            var loaded = _repository!.LoadDay(date);
            _repository.SetSetting("SelectedDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            _selected = date;
            _month = new DateOnly(date.Year, date.Month, 1);
            LoadSelectedDay(loaded);
        }
        catch (Exception exception) { await ShowMessage("无法打开日期", exception.Message); }
    }

    private void RenderCalendar()
    {
        MonthLabel.Text = _month.ToString("yyyy年M月", ExportPlanner.Chinese);
        var offset = ((int)_month.DayOfWeek + 6) % 7;
        // Normal app dates fit the 42-cell view; boundary cells are disabled.
        var firstNumber = _month.DayNumber - offset;
        var from = DateOnly.FromDayNumber(Math.Max(DateOnly.MinValue.DayNumber, firstNumber));
        var to = DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, firstNumber + 41));
        var counts = _repository!.GetDayCounts(from, to);
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        for (var i = 0; i < 7; i++) CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 6; i++) CalendarGrid.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < 42; i++)
        {
            var number = firstNumber + i;
            if (number < DateOnly.MinValue.DayNumber || number > DateOnly.MaxValue.DayNumber) continue;
            var date = DateOnly.FromDayNumber(number);
            var count = counts.GetValueOrDefault(date);
            var selected = date == _selected;
            var content = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = date.Day.ToString(), FontSize = 17, FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(label);
            var marker = new TextBlock
            {
                Text = count > 0 ? (CalendarColumn.ActualWidth > 390 ? $"{count}项" : "●") : date == DateOnly.FromDateTime(DateTime.Today) ? "·" : " ",
                FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center
            };
            content.Children.Add(marker);
            var button = new Button { Content = content, Style = (Style)Application.Current.Resources["CalendarButton"], Tag = date };
            if (selected)
            {
                button.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                button.Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
            }
            else
            {
                button.Background = new SolidColorBrush(Colors.Transparent);
                button.BorderThickness = new Thickness(0);
                button.Opacity = date.Month == _month.Month ? 1 : 0.38;
            }
            AutomationProperties.SetName(button, $"{date:yyyy年M月d日}，{count}项计划");
            ToolTipService.SetToolTip(button, $"{date:yyyy年M月d日} · {count}项计划" + (date == DateOnly.FromDateTime(DateTime.Today) ? " · 今天" : ""));
            button.Click += (_, _) => SelectDate(date);
            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            CalendarGrid.Children.Add(button);
        }
    }

    private async void ChangeMonth(int delta)
    {
        if (!_ready) return;
        try { _month = _month.AddMonths(delta); RenderCalendar(); }
        catch (ArgumentOutOfRangeException) { }
        catch (Exception exception) { await ShowMessage("无法读取日历", exception.Message); }
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(-1);
    private void NextMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(1);
    private void Today_Click(object sender, RoutedEventArgs e) => SelectDate(DateOnly.FromDateTime(DateTime.Today));
    private void PreviousDay_Click(object sender, RoutedEventArgs e) { if (_selected > DateOnly.MinValue) SelectDate(_selected.AddDays(-1)); }
    private void NextDay_Click(object sender, RoutedEventArgs e) { if (_selected < DateOnly.MaxValue) SelectDate(_selected.AddDays(1)); }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AttachItem(new PlanItem());
        Renumber();
        MarkDirty();
        DispatcherQueue.TryEnqueue(() =>
        {
            PlanRows.UpdateLayout();
            PlanScroll.ChangeView(null, PlanScroll.ScrollableHeight, null);
            if (PlanRows.ContainerFromIndex(_items.Count - 1) is DependencyObject container)
                FindTextBox(container)?.Focus(FocusState.Programmatic);
        });
    }

    private static TextBox? FindTextBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box) return box;
            if (FindTextBox(child) is { } found) return found;
        }
        return null;
    }

    private async void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        var id = ((FrameworkElement)sender).Tag as string;
        var item = _items.FirstOrDefault(row => row.Id == id);
        if (item is null) return;
        if (!item.IsBlank)
        {
            if (_showingDialog) return;
            _showingDialog = true;
            try
            {
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "删除这项计划？", Content = "删除后将自动保存。", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary) return;
            }
            finally { _showingDialog = false; }
        }
        item.PropertyChanged -= Item_Changed;
        _items.Remove(item);
        if (_items.Count == 0) AttachItem(new PlanItem());
        Renumber();
        MarkDirty();
    }

    private void Renumber() { for (var i = 0; i < _items.Count; i++) _items[i].Number = i + 1; }

    private void Handle_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        args.Data.Properties[DragKey] = ((FrameworkElement)sender).Tag;
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Properties.ContainsKey(DragKey)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = "移动到此处";
        e.Handled = true;
    }

    private void Row_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Properties.TryGetValue(DragKey, out var value) || value is not string sourceId) return;
        var targetId = ((FrameworkElement)sender).Tag as string;
        var source = _items.FirstOrDefault(item => item.Id == sourceId);
        var target = _items.FirstOrDefault(item => item.Id == targetId);
        if (source is null || target is null || source == target) return;
        _items.Move(_items.IndexOf(source), _items.IndexOf(target));
        Renumber();
        MarkDirty();
        e.Handled = true;
    }

    private void Divider_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var available = Workspace.ActualWidth - 18;
        if (available <= 770) return;
        var width = Math.Clamp(CalendarColumn.ActualWidth + e.HorizontalChange, 290, available - 480);
        _calendarFraction = width / available;
        CalendarColumn.Width = new GridLength(_calendarFraction, GridUnitType.Star);
        EditorColumn.Width = new GridLength(1 - _calendarFraction, GridUnitType.Star);
    }

    private async void Divider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        try { _repository?.SetSetting("CalendarFraction", _calendarFraction.ToString(CultureInfo.InvariantCulture)); }
        catch (Exception exception) { await ShowMessage("未能记住布局", exception.Message); }
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        DoneButton.Focus(FocusState.Programmatic);
        _saveTimer.Stop();
        if (!TrySave()) { await ShowMessage("请先保存计划", "当前保存失败，请点击右下角“重试保存”后再导出。"); return; }
        try
        {
            ExportPane.Open(_repository!, _paths, _selected, this);
            MainPane.Visibility = Visibility.Collapsed;
            ExportPane.Visibility = Visibility.Visible;
        }
        catch (Exception exception) { await ShowMessage("无法打开导出页面", exception.Message); }
    }

    private void RetrySave_Click(object sender, RoutedEventArgs e) => TrySave();

    private async void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (ExportPane.IsExporting)
        {
            args.Cancel = true;
            await ShowMessage("正在导出", "请等待导出结束后再关闭窗口。");
        }
        else if (!TrySave())
        {
            args.Cancel = true;
            await ShowMessage("尚有内容未保存", "保存失败，窗口会保留当前输入。请检查目录权限或磁盘空间，再重试保存。");
        }
    }

    private async Task ShowMessage(string title, string message)
    {
        if (_showingDialog || _closed || DialogService.IsOpen) return;
        _showingDialog = true;
        try { await DialogService.ShowAsync(new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = message, CloseButtonText = "知道了" }); }
        finally { _showingDialog = false; }
    }
}
