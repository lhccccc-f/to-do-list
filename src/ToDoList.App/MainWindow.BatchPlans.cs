using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToDoList.Core.Export;
using ToDoList.Core.Models;

namespace ToDoList.App;

public sealed partial class MainWindow
{
    private async void BatchSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || DialogService.IsOpen) return;
        try
        {
            SavePendingChanges();
            var bookId = _book.Id;
            var content = new TextBox
            {
                Header = "计划内容", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 20000, MaxHeight = 130
            };
            var duration = new TextBox { Header = "预计用时", PlaceholderText = "可留空", MaxLength = 200 };
            var dates = new DateSelectionView();
            dates.Open(_selected, _repository!.GetDayCounts(DateOnly.MinValue, DateOnly.MaxValue, bookId), isBatchAdd: true);
            var addFields = new StackPanel { Spacing = 12, Children = { content, duration, dates } };
            var batches = new StackPanel { Spacing = 8 };
            var confirmation = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 5,
                TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = true };
            var addTab = new Button { Content = "批量添加" };
            var deleteTab = new Button { Content = "删除批量添加" };
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addTab, deleteTab } };
            var fields = new ScrollViewer { Content = addFields, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var addMode = true;
            PlanBatch? selectedBatch = null;
            Border? selectedOutline = null;
            var accent = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            var accentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var quietStyle = (Style)Application.Current.Resources["QuietButton"];
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = $"批量设置 · {_book.Name}",
                Content = new StackPanel { Spacing = 12, Children = { tabs, fields, confirmation, error } }
            };
            DialogService.SetActions(dialog, "返回", "确定");

            void UpdateAction()
            {
                dialog.SecondaryButtonText = addMode ? "确定" : "确认删除";
                dialog.IsSecondaryButtonEnabled = addMode
                    ? !string.IsNullOrWhiteSpace(content.Text) && dates.SelectedDates.Length > 0
                    : selectedBatch is not null;
            }

            void SelectBatch(PlanBatch? batch, Border? outline)
            {
                if (selectedOutline is not null) selectedOutline.BorderBrush = null;
                selectedBatch = batch;
                selectedOutline = outline;
                if (outline is not null) outline.BorderBrush = accent;
                confirmation.Visibility = batch is null ? Visibility.Collapsed : Visibility.Visible;
                if (batch is not null)
                    confirmation.Text = $"删除「{batch.Content}」在 {batch.DateSummary} 的 {batch.RemainingCount} 条计划？单独修改过的条目会保留。";
                UpdateAction();
            }

            void ShowTab(bool adding)
            {
                addMode = adding;
                addTab.Style = adding ? accentStyle : quietStyle;
                deleteTab.Style = adding ? quietStyle : accentStyle;
                error.IsOpen = false;
                SelectBatch(null, null);
                fields.Content = adding ? addFields : batches;
                if (!adding) RefreshBatches();
                fields.ChangeView(null, 0, null);
            }

            content.TextChanged += (_, _) => UpdateAction();
            dates.SelectionChanged += (_, _) => UpdateAction();
            addTab.Click += (_, _) => ShowTab(true);
            deleteTab.Click += (_, _) =>
            {
                try { ShowTab(false); }
                catch (Exception exception) { SetBatchError(error, exception.Message); }
            };
            dialog.SecondaryButtonClick += (_, args) =>
            {
                try
                {
                    if (addMode)
                    {
                        _repository.AddBatch(bookId, dates.SelectedDates, content.Text, duration.Text);
                    }
                    else
                    {
                        args.Cancel = true;
                        if (selectedBatch is null) return;
                        _repository.DeleteBatch(bookId, selectedBatch.Id);
                        SelectBatch(null, null);
                        RefreshBatches();
                        LoadSelectedDay();
                    }
                }
                catch (Exception exception)
                {
                    args.Cancel = true;
                    SetBatchError(error, exception.Message);
                }
            };

            void RefreshBatches()
            {
                batches.Children.Clear();
                foreach (var batch in _repository.LoadBatches(bookId))
                {
                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                    var entry = new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock { Text = batch.Content, TextWrapping = TextWrapping.Wrap, MaxLines = 3,
                                TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = batch.DateSummary, TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = $"剩余 {batch.RemainingDays} 天 · {batch.RemainingCount} 条", FontSize = 12 }
                        }
                    };
                    var select = new Button
                    {
                        Style = quietStyle, Content = entry, HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    AutomationProperties.SetName(select, $"选择批次 {batch.Content} · {batch.DateSummary}");
                    row.Children.Add(select);
                    var delete = new Button
                    {
                        Style = quietStyle,
                        Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    AutomationProperties.SetName(delete, $"删除该批次的 {batch.RemainingCount} 条计划");
                    ToolTipService.SetToolTip(delete, "删除批次");
                    Grid.SetColumn(delete, 1);
                    row.Children.Add(delete);
                    var outline = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = row };
                    select.Click += (_, _) => SelectBatch(batch, outline);
                    delete.Click += (_, _) => SelectBatch(batch, outline);
                    batches.Children.Add(outline);
                }
                if (batches.Children.Count == 0)
                    batches.Children.Add(new TextBlock { Text = "当前日历簿没有可删除的批量计划。", TextWrapping = TextWrapping.Wrap });
            }

            ShowTab(true);
            if (await DialogService.ShowAsync(dialog) == ContentDialogResult.Secondary)
                LoadSelectedDay();
        }
        catch (Exception exception) { await ShowMessage("无法完成批量设置", exception.Message); }
    }

    private async void DeleteSelectedPlans_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || DialogService.IsOpen) return;
        try
        {
            SavePendingChanges();
            var date = _selected;
            var rows = _items.Select(item => item.Snapshot()).ToArray();
            var choices = new StackPanel { Spacing = 6 };
            var selected = new HashSet<string>();
            var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = true };
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = $"批量删除 · {date.ToString("M月d日", ExportPlanner.Chinese)}",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children = { new ScrollViewer { MaxHeight = 360, Content = choices }, error }
                }
            };
            DialogService.SetActions(dialog, "取消", "确认删除（0）");
            dialog.IsSecondaryButtonEnabled = false;
            foreach (var item in rows.Where(item => !item.IsBlank))
            {
                var text = string.IsNullOrWhiteSpace(item.Content) ? "未填写计划内容" : item.Content;
                if (!string.IsNullOrWhiteSpace(item.Duration)) text += $" · {item.Duration}";
                var choice = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = $"{item.Number}. {text}", TextWrapping = TextWrapping.Wrap,
                        MaxLines = 6, TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                ToolTipService.SetToolTip(choice, text);
                choice.Checked += (_, _) => { selected.Add(item.Id); UpdateSelection(); };
                choice.Unchecked += (_, _) => { selected.Remove(item.Id); UpdateSelection(); };
                choices.Children.Add(choice);
            }
            if (choices.Children.Count == 0) choices.Children.Add(new TextBlock { Text = "这一天没有可删除的计划。" });

            void UpdateSelection()
            {
                dialog.SecondaryButtonText = $"确认删除（{selected.Count}）";
                dialog.IsSecondaryButtonEnabled = selected.Count > 0;
            }

            dialog.SecondaryButtonClick += (_, args) =>
            {
                try { _repository!.SaveDay(date, rows.Where(item => !selected.Contains(item.Id)), _book.Id); }
                catch (Exception exception)
                {
                    args.Cancel = true;
                    SetBatchError(error, exception.Message);
                }
            };
            if (await DialogService.ShowAsync(dialog) == ContentDialogResult.Secondary)
                LoadSelectedDay();
        }
        catch (Exception exception) { await ShowMessage("无法删除计划", exception.Message); }
    }

    private static void SetBatchError(InfoBar error, string message)
    {
        error.Message = message;
        error.IsOpen = true;
    }
}
