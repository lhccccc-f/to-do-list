using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ToDoList.App;

internal sealed class WindowSizing : IDisposable
{
    private readonly nint _handle;
    private readonly SubclassProc _callback;
    private readonly int _width;
    private readonly int _height;

    public WindowSizing(Window window, int width, int height)
    {
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _width = width;
        _height = height;
        _callback = Process;
        SetWindowSubclass(_handle, _callback, 1, 0);
    }

    private nint Process(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        const uint wmGetMinMaxInfo = 0x0024;
        if (message == wmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var scale = GetDpiForWindow(window) / 96.0;
            info.MinTrackSize = new Point { X = (int)(_width * scale), Y = (int)(_height * scale) };
            Marshal.StructureToPtr(info, lParam, false);
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose() => RemoveWindowSubclass(_handle, _callback, 1);
    public static double DpiScale(Window window) => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;

    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved; public Point MaxSize; public Point MaxPosition; public Point MinTrackSize; public Point MaxTrackSize; }
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
