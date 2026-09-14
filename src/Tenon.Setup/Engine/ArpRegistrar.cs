using System.IO;
using Microsoft.Win32;
using Tenon.Payload;

namespace Tenon.Setup.Engine;

/// <summary>
/// Registers the product in Add/Remove Programs with Setup.exe as the uninstaller (Burn style),
/// and keeps a copy of Setup.exe in a package cache so uninstall, repair and modify work later.
/// The MSI itself is installed with ARPSYSTEMCOMPONENT=1 so only one entry is visible.
/// </summary>
public static class ArpRegistrar
{
    public const string KeyPrefix = "Tenon_";

    public static string CacheDirectory(SetupManifest m, bool perMachine)
    {
        var root = perMachine
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Tenon", m.UpgradeCode.Trim('{', '}'));
    }

    public static string CachedSetupPath(SetupManifest m, bool perMachine)
        => Path.Combine(CacheDirectory(m, perMachine), SetupEngine.SafeFileName(m.ProductName) + "-Setup.exe");

    public static bool IsRunningFromCache(SetupManifest m)
    {
        var self = Environment.ProcessPath ?? "";
        return self.StartsWith(CacheDirectory(m, true), StringComparison.OrdinalIgnoreCase)
            || self.StartsWith(CacheDirectory(m, false), StringComparison.OrdinalIgnoreCase);
    }

    private static RegistryKey OpenUninstallRoot(bool perMachine, bool writable)
    {
        var hive = RegistryKey.OpenBaseKey(perMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable);
        return key ?? throw new InvalidOperationException("The Uninstall registry key is not accessible.");
    }

    public static void Register(SetupManifest m, bool perMachine, string installDir, SetupLog log)
    {
        var cachePath = CachedSetupPath(m, perMachine);
        var self = Environment.ProcessPath!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            if (!string.Equals(self, cachePath, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, cachePath, overwrite: true);
            log.Info($"Cached setup at {cachePath}");
        }
        catch (Exception ex)
        {
            log.Warn($"Could not cache setup: {ex.Message}. Uninstall from Add/Remove Programs will use msiexec.");
            cachePath = self;
        }

        long sizeKb = 0;
        try
        {
            if (Directory.Exists(installDir))
                sizeKb = Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
        }
        catch
        {
            // ignore
        }

        using var root = OpenUninstallRoot(perMachine, writable: true);
        using var key = root.CreateSubKey(KeyPrefix + m.UpgradeCode.Trim('{', '}'), writable: true)!;
        key.SetValue("DisplayName", m.ProductName);
        key.SetValue("DisplayVersion", m.Version);
        key.SetValue("Publisher", m.Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("DisplayIcon", !string.IsNullOrEmpty(m.MainExe) && File.Exists(Path.Combine(installDir, m.MainExe)) ? Path.Combine(installDir, m.MainExe) : cachePath);
        key.SetValue("UninstallString", $"\"{cachePath}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{cachePath}\" /uninstall /quiet");
        key.SetValue("ModifyPath", $"\"{cachePath}\" /modify");
        key.SetValue("NoModify", m.ArpNoModify || m.Tasks.Count == 0 ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NoRepair", m.ArpNoRepair ? 1 : 0, RegistryValueKind.DWord);
        if (sizeKb > 0) key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
        if (!string.IsNullOrEmpty(m.Url)) key.SetValue("URLInfoAbout", m.Url);
        if (!string.IsNullOrEmpty(m.SupportUrl)) key.SetValue("HelpLink", m.SupportUrl);
        var ver = Version.TryParse(m.MsiVersion, out var v) ? v : new Version(0, 0);
        key.SetValue("VersionMajor", ver.Major, RegistryValueKind.DWord);
        key.SetValue("VersionMinor", ver.Minor, RegistryValueKind.DWord);
        key.SetValue("TenonUpgradeCode", m.UpgradeCode);
        key.SetValue("TenonProductCode", m.ProductCode);
        key.SetValue("TenonScope", perMachine ? "perMachine" : "perUser");
        key.SetValue("TenonSetupPath", cachePath);
        log.Info($"Registered Add/Remove Programs entry ({(perMachine ? "HKLM" : "HKCU")})");
    }

    public static void Unregister(SetupManifest m, bool perMachine, SetupLog log)
    {
        try
        {
            using var root = OpenUninstallRoot(perMachine, writable: true);
            root.DeleteSubKeyTree(KeyPrefix + m.UpgradeCode.Trim('{', '}'), throwOnMissingSubKey: false);
            log.Info("Removed Add/Remove Programs entry");
        }
        catch (Exception ex)
        {
            log.Warn("Could not remove the Add/Remove Programs entry: " + ex.Message);
        }

        var cacheDir = CacheDirectory(m, perMachine);
        if (!Directory.Exists(cacheDir)) return;
        try
        {
            var self = Environment.ProcessPath ?? "";
            if (self.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase))
            {
                // We are running from the cache: delete it after we exit.
                ScheduleDelete(cacheDir, log);
            }
            else
            {
                Directory.Delete(cacheDir, recursive: true);
                log.Info("Removed package cache");
            }
        }
        catch (Exception ex)
        {
            log.Warn("Could not remove the package cache: " + ex.Message);
        }
    }

    /// <summary>Deletes a folder once the current process has exited (used when Setup runs from the cache).</summary>
    public static void ScheduleDelete(string directory, SetupLog log)
    {
        try
        {
            var cmd = $"/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"{directory}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            log.Info($"Scheduled removal of {directory}");
        }
        catch (Exception ex)
        {
            log.Warn("Could not schedule cache removal: " + ex.Message);
        }
    }
}
