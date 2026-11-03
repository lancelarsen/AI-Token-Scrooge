using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AIResetRadar.Infrastructure;

[SupportedOSPlatform("windows")]
internal static class WindowsDirectoryIdentity
{
    internal static string Resolve(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("The app data directory must exist before selecting credential storage.");
        using var handle = CreateFile(full, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("The app data directory could not be identified.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandleEx(handle, 18, out var identity, (uint)Marshal.SizeOf<FileIdInformation>()))
            throw new IOException("The app data directory could not be identified.");
        // A resolved pathname can still differ under Windows package virtualization.
        // Volume + file identity names the directory itself across those aliases.
        return $"{identity.Volume:X16}:{identity.Low:X16}:{identity.High:X16}";
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [StructLayout(LayoutKind.Sequential)]
    struct FileIdInformation { public ulong Volume; public ulong Low; public ulong High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileIdInformation information, uint size);
}
