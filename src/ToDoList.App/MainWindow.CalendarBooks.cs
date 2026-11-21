using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ToDoList.Core.Models;

namespace ToDoList.App;

public sealed partial class MainWindow
{
    private void UpdateBookName()
    {
        ActiveBookName.Text = _book.Name;
        AutomationProperties.SetName(BookButton, $"切换日历簿：{_book.Name}");
        ToolTipService.SetToolTip(BookButton, _book.Name);
    }

    private async void BookFlyout_Opening(object sender, object e)
    {
        BookList.Children.Clear();
        if (!_ready) return;
        try
        {
            foreach (var book in _repository!.LoadBooks())
            {
                var row = new Grid { ColumnSpacing = 4 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                var select = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new TextBlock
                    {
                        Text = book.Name, MaxWidth = 158, TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };
                if (book.Id == _book.Id)
                    select.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                AutomationProperties.SetName(select, book.Id == _book.Id ? $"{book.Name}（当前）" : book.Name);
                ToolTipService.SetToolTip(select, book.Name);
                select.Click += async (_, _) =>
                {
                    BookFlyout.Hide();
                    try { SwitchBook(book); }
                    catch (Exception exception) { await ShowMessage("无法切换日历簿", exception.Message); }
                };
                var rename = new Button
                {
                    Style = (Style)Application.Current.Resources["QuietButton"],
                    Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 }
                };
                ToolTipService.SetToolTip(rename, "重命名");
                AutomationProperties.SetName(rename, $"重命名 {book.Name}");
                rename.Click += async (_, _) =>
                {
                    BookFlyout.Hide();
                    await RenameBookAsync(book);
                };
                Grid.SetColumn(rename, 1);
                var delete = new Button
                {
                    Style = (Style)Application.Current.Resources["QuietButton"],
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 }
                };
                ToolTipService.SetToolTip(delete, "删除日历簿");
                AutomationProperties.SetName(delete, $"删除日历簿 {book.Name}");
                delete.Click += async (_, _) =>
                {
                    BookFlyout.Hide();
                    await DeleteBookAsync(book);
                };
                Grid.SetColumn(delete, 2);
                row.Children.Add(select);
                row.Children.Add(rename);
                row.Children.Add(delete);
                BookList.Children.Add(row);
            }
        }
        catch (Exception exception)
        {
            BookFlyout.Hide();
            await ShowMessage("无法读取日历簿", exception.Message);
        }
    }

    private async void AddBook_Click(object sender, RoutedEventArgs e)
    {
        BookFlyout.Hide();
        if (!_ready) return;
        try
        {
            SavePendingChanges();
            SwitchBook(_repository!.CreateBook());
        }
        catch (Exception exception) { await ShowMessage("无法新增日历簿", exception.Message); }
    }

    private void SwitchBook(CalendarBook book)
    {
        if (book.Id == _book.Id) return;
        SavePendingChanges();
        var date = book.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        var loaded = _repository!.LoadDay(date, book.Id);
        _repository.SetSetting("ActiveBook", book.Id.ToString(CultureInfo.InvariantCulture));
        DisplayBook(book, date, loaded);
    }

    private void DisplayBook(CalendarBook book, DateOnly date, IReadOnlyList<PlanItem> loaded)
    {
        _book = book;
        _selected = date;
        _month = new DateOnly(date.Year, date.Month, 1);
        UpdateBookName();
        LoadSelectedDay(loaded);
    }

    private async Task DeleteBookAsync(CalendarBook book)
    {
        if (DialogService.IsOpen) return;
        var error = new InfoBar { Severity = InfoBarSeverity.Error };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "删除日历簿？",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"将删除「{book.Name}」及其中所有计划和批次。", TextWrapping = TextWrapping.Wrap },
                    error
                }
            }
        };
        DialogService.SetActions(dialog, "确认删除");
        CalendarBook? nextBook = null;
        dialog.SecondaryButtonClick += (_, args) =>
        {
            try
            {
                SavePendingChanges();
                nextBook = _repository!.DeleteBook(book.Id, _book.Id);
            }
            catch (Exception exception)
            {
                args.Cancel = true;
                error.Message = exception.Message;
                error.IsOpen = true;
            }
        };
        try
        {
            if (await DialogService.ShowAsync(dialog) == ContentDialogResult.Secondary)
            {
                if (book.Id == _book.Id && nextBook is not null)
                {
                    var date = nextBook.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
                    DisplayBook(nextBook, date, _repository!.LoadDay(date, nextBook.Id));
                }
            }
            else
            {
                BookFlyout.ShowAt(BookButton);
            }
        }
        catch (Exception exception) { await ShowMessage("无法更新日历簿", exception.Message); }
    }

    private async Task RenameBookAsync(CalendarBook book)
    {
        if (DialogService.IsOpen) return;
        var input = new TextBox { Text = book.Name, Header = "日历簿名", MaxLength = 60 };
        var error = new InfoBar { Severity = InfoBarSeverity.Error };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "重命名日历簿",
            Content = new StackPanel { Spacing = 8, Children = { input, error } }
        };
        DialogService.SetActions(dialog, "保存", submitFrom: input);
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            try
            {
                var name = input.Text.Trim();
                _repository!.RenameBook(book.Id, name);
                if (_book.Id == book.Id)
                {
                    _book = _book with { Name = name };
                    UpdateBookName();
                }
            }
            catch (Exception exception)
            {
                args.Cancel = true;
                error.Message = exception.Message;
                error.IsOpen = true;
            }
        };
        await DialogService.ShowAsync(dialog);
    }

    private void SavePendingChanges()
    {
        _saveTimer.Stop();
        if (!TrySave()) throw new InvalidOperationException("当前计划尚未保存，请点击“重试保存”后再操作。");
    }
}
