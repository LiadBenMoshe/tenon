using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Tenon.CustomAction;

/// <summary>
/// Entry points called by msiexec. Every export catches all exceptions: a .NET exception must never
/// unwind into the Windows Installer custom action server.
/// </summary>
public static class Exports
{
    private const uint Success = 0;
    private const uint UserExit = 1602;
    private const uint Failure = 1603;

    // ------------------------------------------------------------- hooks

    /// <summary>Immediate: unpacks the hook host and hooks, then runs the Prepare stage.</summary>
    [UnmanagedCallersOnly(EntryPoint = "TenonPrepare")]
    public static uint TenonPrepare(uint hInstall) => Guard(hInstall, s =>
    {
        var runId = s["TenonRunId"];
        if (runId.Length == 0)
        {
            runId = Guid.NewGuid().ToString("N");
            s["TenonRunId"] = runId;
        }
        var hooksRoot = Path.Combine(Path.GetTempPath(), "Tenon", "hooks");
        var hooksDir = Path.Combine(hooksRoot, runId);
        CleanStaleRuns(hooksRoot, runId);
        Directory.CreateDirectory(hooksDir);
        s["TenonHooksDir"] = hooksDir;

        if (!s.ExtractBinary("TenonHookHost", Path.Combine(hooksDir, "tenon-hookhost.exe")))
        {
            s.Log("No hook host in this package; nothing to prepare.");
            return Success;
        }
        var zip = Path.Combine(hooksDir, "hooks.zip");
        if (!s.ExtractBinary("TenonHooks", zip)) return Success;
        var target = Path.Combine(hooksDir, "hooks");
        if (Directory.Exists(target)) Directory.Delete(target, true);
        ZipFile.ExtractToDirectory(zip, target);
        s.Log($"Hooks unpacked to {target}");

        if (s["TenonPrepareHooks"] != "1") return Success;
        if (s["TENON_PREPARED"] == "1") return Success;

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HooksDir"] = hooksDir,
            ["Assembly"] = s["TenonHooksAssembly"],
            ["ProductName"] = s["ProductName"],
            ["Version"] = s["ProductVersion"],
            ["InstallDir"] = s["INSTALLDIR"],
            ["AllUsers"] = s["ALLUSERS"],
            ["Upgrade"] = s["TENON_UPGRADE_DETECTED"],
            ["Remove"] = s["REMOVE"],
            ["Reinstall"] = s["REINSTALL"],
            ["UILevel"] = s["UILevel"],
            ["Tasks"] = s["ADDLOCAL"],
        };
        return HookLauncher.Run(s, "Prepare", data);
    });

    /// <summary>Deferred/rollback: runs the stage named in CustomActionData.</summary>
    [UnmanagedCallersOnly(EntryPoint = "TenonHook")]
    public static uint TenonHook(uint hInstall) => Guard(hInstall, s =>
    {
        var data = s.CustomActionData();
        var stage = data.GetValueOrDefault("Stage", "");
        if (s.IsRollback) stage = "Rollback";
        if (stage.Length == 0) { s.Log("No stage in CustomActionData."); return Success; }
        return HookLauncher.Run(s, stage, data);
    });

    // ------------------------------------------------------------- built-ins

    /// <summary>Immediate, before LaunchConditions: sets TENON_DOTNET_OK when the required .NET runtime exists.</summary>
    [UnmanagedCallersOnly(EntryPoint = "TenonCheckDotNet")]
    public static uint TenonCheckDotNet(uint hInstall) => Guard(hInstall, s =>
    {
        // "8.0;desktop;x64"
        var spec = s["TenonDotNetRequired"].Split(';');
        if (spec.Length < 3 || !Version.TryParse(spec[0], out var required)) return Success;
        var framework = spec[1] == "desktop" ? "Microsoft.WindowsDesktop.App" : "Microsoft.NETCore.App";
        var arch = spec[2];
        var pf = arch == "x86" && Environment.Is64BitOperatingSystem
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            : Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var dir = Path.Combine(pf, "dotnet", "shared", framework);
        var ok = false;
        if (Directory.Exists(dir))
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                var dash = name.IndexOf('-');
                if (dash > 0) name = name.Substring(0, dash);
                if (Version.TryParse(name, out var v) && v.Major == required.Major && v >= required) { ok = true; break; }
            }
        }
        s.Log($".NET check: {framework} {required} {(ok ? "found" : "missing")} in {dir}");
        if (ok) s["TENON_DOTNET_OK"] = "1";
        return Success;
    });

    /// <summary>Immediate, after InstallFinalize: launches the application when LAUNCHAPP=1.</summary>
    [UnmanagedCallersOnly(EntryPoint = "TenonLaunch")]
    public static uint TenonLaunch(uint hInstall) => Guard(hInstall, s =>
    {
        var exe = Path.Combine(s["INSTALLDIR"], s["TenonMainExe"]);
        if (!File.Exists(exe)) return Success;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        s.Log($"Launched {exe}");
        return Success;
    });

    /// <summary>Deferred (impersonated) on uninstall: removes user data folders listed in CustomActionData.</summary>
    [UnmanagedCallersOnly(EntryPoint = "TenonDeleteAppData")]
    public static uint TenonDeleteAppData(uint hInstall) => Guard(hInstall, s =>
    {
        var data = s.CustomActionData();
        foreach (var path in data.GetValueOrDefault("Paths", "").Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = path.TrimEnd('\\');
            try
            {
                if (Directory.Exists(p)) { Directory.Delete(p, true); s.Log($"Deleted {p}"); }
            }
            catch (Exception ex)
            {
                s.Log($"Could not delete {p}: {ex.Message}");
            }
        }
        return Success;
    });

    /// <summary>Deferred (no impersonation): adds or removes Windows Firewall rules through netsh.</summary>
    [UnmanagedCallersOnly(EntryPoint = "TenonFirewall")]
    public static uint TenonFirewall(uint hInstall) => Guard(hInstall, s =>
    {
        var data = s.CustomActionData();
        var remove = s.IsRollback || data.GetValueOrDefault("Action", "add") == "remove";
        foreach (var rule in data.GetValueOrDefault("Rules", "").Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            // name\tdir\tprotocol\tport\tprogram\tprofiles
            var f = rule.Split('\t');
            if (f.Length < 6) continue;
            var name = f[0];
            string args;
            if (remove)
            {
                args = $"advfirewall firewall delete rule name=\"{name}\"";
            }
            else
            {
                args = $"advfirewall firewall add rule name=\"{name}\" dir={f[1]} action=allow enable=yes";
                if (f[2].Length > 0 && f[2] != "any") args += $" protocol={f[2]}";
                if (f[3].Length > 0) args += $" localport={f[3]}";
                if (f[4].Length > 0) args += $" program=\"{f[4]}\"";
                if (f[5].Length > 0) args += $" profile={f[5]}";
            }
            var psi = new ProcessStartInfo("netsh.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit();
            s.Log($"netsh {args} -> {p?.ExitCode} {output.Trim().Replace("\r\n", " ")}");
            if (!remove && p?.ExitCode != 0)
            {
                s.Error($"Could not add the firewall rule '{name}'.");
                return Failure;
            }
        }
        return Success;
    });

    // ------------------------------------------------------------- plumbing

    /// <summary>Deferred actions cannot know when a run is over, so each new run removes folders older than an hour.</summary>
    private static void CleanStaleRuns(string hooksRoot, string currentRunId)
    {
        try
        {
            if (!Directory.Exists(hooksRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(hooksRoot))
            {
                if (Path.GetFileName(dir) == currentRunId) continue;
                if (Directory.GetLastWriteTimeUtc(dir) > DateTime.UtcNow.AddHours(-1)) continue;
                try { Directory.Delete(dir, true); } catch { /* in use */ }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static uint Guard(uint hInstall, Func<MsiSession, uint> action)
    {
        var session = new MsiSession(hInstall);
        try
        {
            return action(session);
        }
        catch (Exception ex)
        {
            try { session.Error("Tenon custom action failed: " + ex.Message); } catch { /* ignore */ }
            return Failure;
        }
    }
}
