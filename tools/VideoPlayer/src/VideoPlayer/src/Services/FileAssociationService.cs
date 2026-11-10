using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

/// <summary>
/// 按用户（HKCU）注册文件关联，无需管理员权限。
/// Windows 10/11 出于安全考虑不允许程序静默抢占默认应用，
/// 因此这里负责登记“打开方式”和默认应用候选，最终默认项由用户在系统设置中确认。
/// </summary>
public static class FileAssociationService
{
    public const string ProgId = "VideoPlayer.Media";
    public const string AppName = "VideoPlayer";

    private const string ApplicationsKey = @"Applications\VideoPlayer.exe";
    private const string CapabilitiesKey = @"Software\VideoPlayer\Capabilities";

    private static string ExePath
        => Environment.ProcessPath
           ?? Process.GetCurrentProcess().MainModule?.FileName
           ?? string.Empty;

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            var value = key?.GetValue(null) as string;
            return !string.IsNullOrEmpty(value)
                   && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool Register()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");

            using (var progId = classes.CreateSubKey(ProgId))
            {
                progId.SetValue(null, "视频文件");
                using (var icon = progId.CreateSubKey("DefaultIcon"))
                {
                    icon.SetValue(null, $"\"{exe}\",0");
                }

                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue(null, $"\"{exe}\" \"%1\"");
            }

            using (var app = classes.CreateSubKey(ApplicationsKey))
            {
                using (var command = app.CreateSubKey(@"shell\open\command"))
                {
                    command.SetValue(null, $"\"{exe}\" \"%1\"");
                }

                using var supported = app.CreateSubKey("SupportedTypes");
                foreach (var ext in MediaFormats.AssociableExtensions)
                {
                    supported.SetValue(ext, string.Empty);
                }
            }

            foreach (var ext in MediaFormats.AssociableExtensions)
            {
                using var extKey = classes.CreateSubKey(ext);
                if (extKey.GetValue(null) is null)
                {
                    extKey.SetValue(null, ProgId);
                }

                using var openWith = extKey.CreateSubKey("OpenWithProgids");
                openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registered.SetValue(AppName, CapabilitiesKey);
            }

            using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
            {
                capabilities.SetValue("ApplicationName", AppName);
                capabilities.SetValue("ApplicationDescription", "轻量、现代的多格式视频播放器");
                using var associations = capabilities.CreateSubKey("FileAssociations");
                foreach (var ext in MediaFormats.AssociableExtensions)
                {
                    associations.SetValue(ext, ProgId);
                }
            }

            NotifyShell();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Unregister()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classes is not null)
            {
                classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree(ApplicationsKey, throwOnMissingSubKey: false);

                foreach (var ext in MediaFormats.AssociableExtensions)
                {
                    using var extKey = classes.OpenSubKey(ext, writable: true);
                    if (extKey is null)
                    {
                        continue;
                    }

                    if (string.Equals(extKey.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase))
                    {
                        extKey.DeleteValue(string.Empty, throwOnMissingValue: false);
                    }

                    using (var openWith = extKey.OpenSubKey("OpenWithProgids", writable: true))
                    {
                        openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                    }

                    using var openWithCheck = extKey.OpenSubKey("OpenWithProgids");
                    if (openWithCheck is not null && openWithCheck.ValueCount == 0)
                    {
                        extKey.DeleteSubKeyTree("OpenWithProgids", throwOnMissingSubKey: false);
                    }
                }
            }

            using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            {
                registered?.DeleteValue(AppName, throwOnMissingValue: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\VideoPlayer", throwOnMissingSubKey: false);

            NotifyShell();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>打开 Windows“默认应用”设置，由用户确认关联。</summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_IDLIST = 0x0000;
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // ignore
        }
    }
}
