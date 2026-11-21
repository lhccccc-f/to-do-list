using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ToDoList.Core.Export;
using ToDoList.Core.Models;
using ToDoList.Core.Storage;
using Windows.Storage.Pickers;

namespace ToDoList.App;

public sealed partial class ExportView : UserControl
{
    private const string FolderSetting = "LastExportFolder";
    private PlanRepository? _repository;
    private AppPaths? _paths;
    private Window? _owner;
    private int _bookId;
    private string? _customName;
    private string _folder = "";

    public event EventHandler? BackRequested;
    public bool IsExporting { get; private set; }

    public ExportView()
    {
        InitializeComponent();
        ExportDates.SelectionChanged += (_, _) => RefreshName();
    }

    public void Open(PlanRepository repository, AppPaths paths, CalendarBook book, DateOnly selected, Window owner)
    {
        _repository = repository;
        _paths = paths;
        _owner = owner;
        _bookId = book.Id;
        _customName = null;
        BookLabel.Text = book.Name;
        ShowCheckboxes.IsChecked = true;
        ExportNotice.IsOpen = false;
        ResultButtons.Visibility = Visibility.Collapsed;
        ResultButtons.Children.Clear();
        ExportStatus.Text = "计划已保存，可以开始导出。";
        RestoreFolder();
        ExportDates.Open(selected, repository.GetDayCounts(DateOnly.MinValue, DateOnly.MaxValue, _bookId));
    }

    private string FileName => _customName ?? ExportPlanner.TitleFor(ExportDates.SelectedDates);

    private void RefreshName()
    {
        FileNameDisplay.Text = FileName;
        ResetNameButton.IsEnabled = _customName is not null;
    }

    private async void EditName_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { Header = "文件名", Text = FileName, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(input, "CustomFileNameInput");
        var error = new InfoBar { Severity = InfoBarSeverity.Error };
        var fields = new StackPanel { Spacing = 8, Children = { input, error } };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "编辑文件名", Content = fields
        };
        DialogService.SetActions(dialog, "取消", "保存", defaultToExecute: true);
        string? validName = null;
        dialog.SecondaryButtonClick += (_, args) =>
        {
            try { validName = ExportPlanner.ValidateFileName(input.Text); }
            catch (InvalidOperationException exception)
            {
                args.Cancel = true;
                error.Message = exception.Message;
                error.IsOpen = true;
            }
        };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        if (await DialogService.ShowAsync(dialog) == ContentDialogResult.Secondary)
        {
            _customName = validName;
            RefreshName();
        }
    }

    private void ResetName_Click(object sender, RoutedEventArgs e)
    {
        _customName = null;
        RefreshName();
    }

    private void RestoreFolder()
    {
        _folder = _paths!.ExportDirectory;
        var saved = _repository!.GetSetting(FolderSetting);
        if (saved is not null)
        {
            string? previous = null;
            try
            {
                if (saved.StartsWith("relative:", StringComparison.Ordinal))
                {
                    var path = Path.GetFullPath(saved["relative:".Length..], _paths.Root);
                    if (IsInsideProgram(path)) previous = path;
                }
                else if (saved.StartsWith("absolute:", StringComparison.Ordinal))
                {
                    var path = saved["absolute:".Length..];
                    if (Path.IsPathFullyQualified(path)) previous = path;
                }
            }
            catch (ArgumentException) { }
            if (Directory.Exists(previous)) _folder = previous!;
            else ShowNotice("上次选择的文件夹不可用", "已切换至默认的 Exports 文件夹。你也可以选择其他位置。", InfoBarSeverity.Warning);
        }
        RefreshFolder();
    }

    private bool IsInsideProgram(string path)
    {
        var relative = Path.GetRelativePath(_paths!.Root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private void RefreshFolder() => FolderPath.Text = IsInsideProgram(_folder)
        ? Path.GetRelativePath(_paths!.Root, _folder) + "（程序文件夹内）" : _folder;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!IsExporting) BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SettingsIdentifier = "ToDoListExport"
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner!));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            var setting = IsInsideProgram(folder.Path)
                ? "relative:" + Path.GetRelativePath(_paths!.Root, folder.Path)
                : "absolute:" + folder.Path;
            _repository!.SetSetting(FolderSetting, setting);
            _folder = folder.Path;
            RefreshFolder();
            ExportNotice.IsOpen = false;
        }
        catch (Exception exception) { ShowNotice("未能选择文件夹", exception.Message); }
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
            var formats = (WordFormat.IsChecked == true ? ExportFormats.Word : 0)
                | (PdfFormat.IsChecked == true ? ExportFormats.Pdf : 0)
                | (JpgFormat.IsChecked == true ? ExportFormats.Jpg : 0);
            var name = ExportPlanner.ValidateFileName(FileName);
            var plan = ExportPlanner.Create(ExportDates.SelectedDates, date => _repository.LoadDay(date, _bookId),
                IncludeEmpty.IsChecked == true, ShowCheckboxes.IsChecked == true);
            ExportProgress.Visibility = Visibility.Visible;
            ExportProgress.IsActive = true;
            ExportStatus.Text = "正在生成文件，请稍候…";
            var service = new ExportService(new DocumentExporter(_paths.FontFile), PdfImageRenderer.RenderJpgAsync);
            var files = await service.ExportAsync(plan, _folder, name, formats, ConfirmOverwriteAsync);
            if (files.Count == 0)
            {
                ExportStatus.Text = "已取消导出。";
                return;
            }
            var imageCount = files.Count(file => Path.GetExtension(file) == ".jpg");
            var result = imageCount > 0 ? $"已导出 {plan.Days.Count} 天的计划，含 {imageCount} 张图片。" : $"已导出 {plan.Days.Count} 天的计划。";
            ShowNotice("导出完成", result, InfoBarSeverity.Success);
            ExportStatus.Text = "文件已保存，可以打开或继续选择日期。";
            ResultButtons.Children.Clear();
            foreach (var group in files.GroupBy(Path.GetExtension))
            {
                var label = group.Key switch
                {
                    ".pdf" => "打开 PDF",
                    ".jpg" when imageCount > 1 => "打开第 1 张图片",
                    ".jpg" => "打开图片",
                    _ => "打开 Word"
                };
                AddResultButton(label, group.First());
            }
            AddResultButton("打开所在文件夹", _folder);
            ResultButtons.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            ExportStatus.Text = "导出未完成，计划数据仍已保存。";
            ShowNotice("请检查导出设置", exception.Message);
        }
        finally
        {
            IsExporting = false;
            ExportForm.IsEnabled = BackButton.IsEnabled = ConfirmExportButton.IsEnabled = true;
            ExportProgress.IsActive = false;
            ExportProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<bool> ConfirmOverwriteAsync(IReadOnlyList<string> paths)
    {
        var files = string.Join("\n", paths.Take(8).Select(Path.GetFileName));
        if (paths.Count > 8) files += $"\n另有 {paths.Count - 8} 个同名文件";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "覆盖已有文件？",
            Content = new TextBlock { Text = files, TextWrapping = TextWrapping.Wrap }
        };
        DialogService.SetActions(dialog, "取消", $"覆盖 {paths.Count} 个文件");
        return await DialogService.ShowAsync(dialog) == ContentDialogResult.Secondary;
    }

    private void AddResultButton(string label, string path)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception exception) { ShowNotice("无法打开，请在保存位置手动查看", exception.Message); }
        };
        ResultButtons.Children.Add(button);
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        ExportNotice.Severity = severity;
        ExportNotice.Title = title;
        ExportNotice.Message = message;
        ExportNotice.IsOpen = true;
    }
}
