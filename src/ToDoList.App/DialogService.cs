using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ToDoList.App;

internal static class DialogService
{
    public static bool IsOpen { get; private set; }

    public static void SetActions(ContentDialog dialog, string cancel, string execute, bool defaultToExecute = false)
    {
        // WinUI lays out Primary, Secondary, and Close in that order.
        dialog.PrimaryButtonText = cancel;
        dialog.SecondaryButtonText = execute;
        dialog.DefaultButton = defaultToExecute ? ContentDialogButton.Secondary : ContentDialogButton.Primary;
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            dialog.Hide();
            args.Handled = true;
        };
        dialog.KeyboardAccelerators.Add(escape);
    }

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (IsOpen) return ContentDialogResult.None;
        IsOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { IsOpen = false; }
    }
}
