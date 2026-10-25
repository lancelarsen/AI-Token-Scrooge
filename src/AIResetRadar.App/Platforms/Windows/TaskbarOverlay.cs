using System.Runtime.InteropServices;
namespace AIResetRadar.App.Platforms.Windows;

static class TaskbarOverlay
{
    public static void Set(nint window, nint icon, string description)
    {
        ITaskbarList3? taskbar = null;
        try
        {
            taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"))!)!;
            taskbar.HrInit(); taskbar.SetOverlayIcon(window, icon, description);
        }
        catch (COMException) { /* Explorer can be restarting; the recreated button triggers a retry. */ }
        finally { if (taskbar is not null) Marshal.ReleaseComObject(taskbar); }
    }
    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3
    {
        void HrInit(); void AddTab(nint hwnd); void DeleteTab(nint hwnd); void ActivateTab(nint hwnd); void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint hwnd, ulong completed, ulong total); void SetProgressState(nint hwnd, uint flags);
        void RegisterTab(nint tab, nint main); void UnregisterTab(nint tab); void SetTabOrder(nint tab, nint before); void SetTabActive(nint tab, nint main, uint flags);
        void ThumbBarAddButtons(nint hwnd, uint count, nint buttons); void ThumbBarUpdateButtons(nint hwnd, uint count, nint buttons); void ThumbBarSetImageList(nint hwnd, nint imageList);
        void SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }
}
