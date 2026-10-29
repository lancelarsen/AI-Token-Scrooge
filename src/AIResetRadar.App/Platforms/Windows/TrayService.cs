using System.Runtime.InteropServices;
using AIResetRadar.Core;
using Microsoft.UI.Xaml;
namespace AIResetRadar.App.Platforms.Windows;

public sealed class TrayService : IDisposable
{
    const uint Callback = 0x8001;
    readonly nint hwnd;
    readonly SubclassProc callback;
    readonly uint explorerRestart;
    readonly uint taskbarCreated;
    readonly nint brandIcon;
    readonly nint largeIcon;
    readonly Dictionary<ReminderSeverity, nint> alertIcons = new();
    readonly Dictionary<ReminderSeverity, nint> trayIcons = new();
    nint overlayIcon;
    NotifyIconData data;
    (string Tip, ReminderSeverity? Severity, bool Unknown)? lastStatus;
    public event Action? Open;
    public event Action? Refresh;
    public event Action? Exit;
    public event Action? ResumeOrClockChanged;
    public TrayService(Microsoft.UI.Xaml.Window window)
    {
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        callback = WindowProc;
        explorerRestart = RegisterWindowMessage("TaskbarCreated"); taskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");
        var dpi = GetDpiForWindow(hwnd);
        var smallSize = GetSystemMetricsForDpi(49, dpi);
        var largeSize = GetSystemMetricsForDpi(11, dpi);
        brandIcon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "app.ico"), 1, smallSize, smallSize, 0x10);
        largeIcon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "app.ico"), 1, largeSize, largeSize, 0x10);
        SendMessage(hwnd, 0x0080, 0, brandIcon);
        SendMessage(hwnd, 0x0080, 1, largeIcon);
        foreach (var level in Enum.GetValues<ReminderSeverity>())
        {
            trayIcons[level] = LoadImage(0, Path.Combine(AppContext.BaseDirectory, $"tray-{level.ToString().ToLowerInvariant()}.ico"), 1, smallSize, smallSize, 0x10);
            alertIcons[level] = LoadImage(0, Path.Combine(AppContext.BaseDirectory, $"alert-{level.ToString().ToLowerInvariant()}.ico"), 1, smallSize, smallSize, 0x10);
        }
        data = new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1, Flags = 1 | 2 | 4, Message = Callback,
            Icon = brandIcon, Tip = "AI Token Scrooge · Monitoring", Info = "", InfoTitle = "" };
        if (!SetWindowSubclass(hwnd, callback, 1, 0)) throw new InvalidOperationException("Windows tray integration could not attach to the app window.");
        if (!Shell_NotifyIcon(0, ref data)) throw new InvalidOperationException("Windows did not create the Scrooge tray icon.");
    }
    public void Update(string tip, ReminderSeverity? severity, bool unknown)
    {
        var displayTip = tip.Length > 127 ? tip[..127] : tip;
        var status = (displayTip, severity, unknown);
        if (lastStatus == status) return;
        data.Tip = displayTip;
        data.Icon = severity.HasValue || unknown ? trayIcons[severity ?? ReminderSeverity.Info] : brandIcon;
        overlayIcon = severity.HasValue || unknown ? alertIcons[severity ?? ReminderSeverity.Info] : 0;
        if (Shell_NotifyIcon(1, ref data)) lastStatus = status;
        TaskbarOverlay.Set(hwnd, overlayIcon, tip);
    }
    nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        if (message == explorerRestart) { Shell_NotifyIcon(0, ref data); TaskbarOverlay.Set(hwnd, overlayIcon, data.Tip); }
        if (message == taskbarCreated) TaskbarOverlay.Set(hwnd, overlayIcon, data.Tip);
        if (message == 0x001E || message == 0x0218 && wParam is 7 or 18) ResumeOrClockChanged?.Invoke();
        if (message == Callback)
        {
            if ((uint)lParam == 0x0202) Open?.Invoke();
            else if ((uint)lParam == 0x0203) Open?.Invoke();
            else if ((uint)lParam == 0x0205) Menu();
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    void Menu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, 1, "Open AI Token Scrooge"); AppendMenu(menu, 0, 2, "Refresh usage"); AppendMenu(menu, 0x800, 0, ""); AppendMenu(menu, 0, 3, "Exit Scrooge");
            GetCursorPos(out var point); SetForegroundWindow(hwnd);
            var selected = TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, hwnd, 0);
            if (selected == 1) Open?.Invoke(); else if (selected == 2) Refresh?.Invoke(); else if (selected == 3) Exit?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose() { Shell_NotifyIcon(2, ref data); RemoveWindowSubclass(hwnd, callback, 1); if (brandIcon != 0) DestroyIcon(brandIcon); if (largeIcon != 0) DestroyIcon(largeIcon); foreach (var icon in alertIcons.Values.Concat(trayIcons.Values)) if (icon != 0) DestroyIcon(icon); }
    delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint action, ref NotifyIconData data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadImage(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string label);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
}
