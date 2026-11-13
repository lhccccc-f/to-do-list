using Microsoft.UI.Xaml.Controls;

namespace ToDoList.App;

internal static class DialogService
{
    public static bool IsOpen { get; private set; }

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (IsOpen) return ContentDialogResult.None;
        IsOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { IsOpen = false; }
    }
}
