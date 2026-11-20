using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
            dates.Open(_selected, _repository!.GetDayCounts(DateOnly.MinValue, DateOnly.MaxValue, bookId), selectWeek: false);
            var addFields = new StackPanel { Spacing = 12, Children = { content, duration, dates } };
            var batches = new StackPanel { Spacing = 8 };
            var confirmation = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
            var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = true };
            var deleteFields = new StackPanel { Spacing = 12, Children = { confirmation, batches } };
            var tabs = new Pivot();
            tabs.Items.Add(new PivotItem { Header = "批量添加", Margin = new Thickness(0),
                Content = new ScrollViewer { Content = addFields, MaxHeight = 464, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            tabs.Items.Add(new PivotItem { Header = "删除批量添加", Margin = new Thickness(0),
                Content = new ScrollViewer { Content = deleteFields, MaxHeight = 464, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            tabs.SelectedIndex = 0;
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = $"批量设置 · {_book.Name}",
                Content = new StackPanel { Children = { tabs, error } },
                PrimaryButtonText = "确定", CloseButtonText = "返回", DefaultButton = ContentDialogButton.Close
            };
            tabs.SelectionChanged += (_, _) =>
            {
                error.IsOpen = false;
                confirmation.Visibility = Visibility.Collapsed;
                dialog.PrimaryButtonText = tabs.SelectedIndex == 0 ? "确定" : "";
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    _repository.AddBatch(bookId, dates.SelectedDates, content.Text, duration.Text);
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
                    row.Children.Add(new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock { Text = batch.Content, TextWrapping = TextWrapping.Wrap, MaxLines = 3,
                                TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = batch.DateSummary, TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = $"剩余 {batch.RemainingCount} 条", FontSize = 12 }
                        }
                    });
                    var delete = new Button
                    {
                        Style = (Style)Application.Current.Resources["QuietButton"],
                        Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    AutomationProperties.SetName(delete, $"删除该批次的 {batch.RemainingCount} 条计划");
                    ToolTipService.SetToolTip(delete, "删除批次");
                    Grid.SetColumn(delete, 1);
                    row.Children.Add(delete);
                    delete.Click += (_, _) => ConfirmBatchDelete(batch);
                    batches.Children.Add(row);
                }
                if (batches.Children.Count == 0)
                    batches.Children.Add(new TextBlock { Text = "当前日历簿没有可删除的批量计划。", TextWrapping = TextWrapping.Wrap });
            }

            void ConfirmBatchDelete(PlanBatch batch)
            {
                error.IsOpen = false;
                confirmation.Children.Clear();
                confirmation.Children.Add(new TextBlock
                {
                    Text = $"删除该批次剩余的 {batch.RemainingCount} 条计划？单独改过的条目会保留。",
                    TextWrapping = TextWrapping.Wrap
                });
                var remove = new Button { Content = "确认删除" };
                var cancel = new Button { Content = "取消" };
                confirmation.Children.Add(new StackPanel
                    { Orientation = Orientation.Horizontal, Spacing = 8, Children = { remove, cancel } });
                cancel.Click += (_, _) => confirmation.Visibility = Visibility.Collapsed;
                remove.Click += (_, _) =>
                {
                    try
                    {
                        _repository.DeleteBatch(bookId, batch.Id);
                        confirmation.Visibility = Visibility.Collapsed;
                        RefreshBatches();
                        LoadSelectedDay();
                    }
                    catch (Exception exception) { SetBatchError(error, exception.Message); }
                };
                confirmation.Visibility = Visibility.Visible;
                // The confirmation is above the list, including when the list is scrolled.
                ((ScrollViewer)((PivotItem)tabs.Items[1]).Content).ChangeView(null, 0, null);
                cancel.Focus(FocusState.Programmatic);
            }

            RefreshBatches();
            if (await DialogService.ShowAsync(dialog) == ContentDialogResult.Primary)
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
                },
                PrimaryButtonText = "确认删除（0）", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false
            };
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
                dialog.PrimaryButtonText = $"确认删除（{selected.Count}）";
                dialog.IsPrimaryButtonEnabled = selected.Count > 0;
            }

            dialog.PrimaryButtonClick += (_, args) =>
            {
                try { _repository!.SaveDay(date, rows.Where(item => !selected.Contains(item.Id)), _book.Id); }
                catch (Exception exception)
                {
                    args.Cancel = true;
                    SetBatchError(error, exception.Message);
                }
            };
            if (await DialogService.ShowAsync(dialog) == ContentDialogResult.Primary)
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
