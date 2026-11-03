using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AIResetRadar.Infrastructure;

/// <summary>Applies only to app-owned credential directories, never arbitrary data trees.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCredentialDirectory
{
    internal static void RequireCurrentOwner(string path)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Windows account unavailable.");
        var owner = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier))
            : new FileInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (!user.Equals(owner)) throw new UnauthorizedAccessException("Credential path belongs to another Windows account.");
    }

    internal static void RejectReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Credential storage cannot use redirected files or directories.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void Ensure(string path)
    {
        RejectReparsePoints(path);
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Windows account unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (!directory.Exists) directory.Create(security);
        else
        {
            var owner = directory.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (!user.Equals(owner)) throw new UnauthorizedAccessException("Credential directory belongs to another Windows account.");
            directory.SetAccessControl(security);
        }
        RejectReparsePoints(path);
    }

    internal static void RestrictFile(string path)
    {
        RejectReparsePoints(path);
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Windows account unavailable.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
