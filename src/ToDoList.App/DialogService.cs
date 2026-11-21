using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ToDoList.App;

internal static class DialogService
{
    public static bool IsOpen { get; private set; }

    public static void SetActions(ContentDialog dialog, string execute, TextBox? submitFrom = null)
    {
        // WinUI lays out Primary, Secondary, and Close in that order.
        dialog.PrimaryButtonText = "取消";
        dialog.SecondaryButtonText = execute;
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["CancelButtonStyle"];
        dialog.SecondaryButtonStyle = (Style)Application.Current.Resources["ExecuteButtonStyle"];
        // The default-button state replaces the template button's explicit style.
        dialog.DefaultButton = ContentDialogButton.None;
        if (submitFrom is not null)
        {
            var enter = new KeyboardAccelerator { Key = VirtualKey.Enter, ScopeOwner = submitFrom };
            enter.Invoked += (_, args) =>
            {
                var button = FindActionButton(dialog);
                if (button?.IsEnabled == true &&
                    new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) is IInvokeProvider action)
                {
                    action.Invoke();
                    args.Handled = true;
                }
            };
            submitFrom.KeyboardAccelerators.Add(enter);
        }
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            dialog.Hide();
            args.Handled = true;
        };
        dialog.KeyboardAccelerators.Add(escape);
    }

    private static Button? FindActionButton(DependencyObject element)
    {
        if (element is Button { Name: "SecondaryButton" } button) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var found = FindActionButton(VisualTreeHelper.GetChild(element, index));
            if (found is not null) return found;
        }
        return null;
    }

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (IsOpen) return ContentDialogResult.None;
        IsOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { IsOpen = false; }
    }
}
