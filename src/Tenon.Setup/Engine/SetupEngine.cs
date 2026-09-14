using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Tenon.Native;
using Tenon.Payload;
using Tenon.Setup.Engine.Elevation;
using Tenon.Setup.Engine.Prereqs;

namespace Tenon.Setup.Engine;

public sealed record SetupProgress(int Percent, string Status, string? Detail);

public sealed class SetupResult
{
    public int ExitCode { get; set; }
    public bool RebootRequired { get; set; }
    public bool Cancelled => ExitCode == (int)MsiApi.ERROR_INSTALL_USEREXIT;
    public bool Succeeded => ExitCode == 0 || ExitCode == (int)MsiApi.ERROR_SUCCESS_REBOOT_REQUIRED || ExitCode == (int)MsiApi.ERROR_SUCCESS_REBOOT_INITIATED;
    public string? ErrorMessage { get; set; }
    public string? Hint { get; set; }
}

/// <summary>
/// Orchestrates one setup run: reads the payload, detects installed versions, resolves the options
/// chosen in the UI (or on the command line), installs prerequisites and drives Windows Installer,
/// elevating a helper copy of this process when administrator rights are needed.
/// </summary>
public sealed class SetupEngine : IDisposable
{
    private readonly PayloadReader _payload;
    private string? _extractedMsi;

    public SetupEngine(PayloadReader payload, SetupManifest manifest, SetupArgs args, SetupLog log)
    {
        _payload = payload;
        Manifest = manifest;
        Args = args;
        Log = log;
        TempDir = Path.Combine(Path.GetTempPath(), "Tenon", manifest.UpgradeCode.Trim('{', '}'), Process.GetCurrentProcess().Id.ToString());
        Directory.CreateDirectory(TempDir);

        Installed = InstalledProduct.FindByUpgradeCode(manifest.UpgradeCode).OrderByDescending(p => p.Version).FirstOrDefault();
        PerMachine = manifest.Scope switch
        {
            "perMachine" => true,
            "perUser" => false,
            _ => Installed?.PerMachine ?? manifest.DefaultScope == "perMachine",
        };
        if (args.Properties.TryGetValue("ALLUSERS", out var allUsers) && manifest.Scope == "either")
            PerMachine = allUsers == "1";

        InstallDir = Installed?.InstallLocation?.TrimEnd('\\') ?? FolderResolver.Resolve(manifest.InstallDir, PerMachine, manifest.Arch);
        if (args.Properties.TryGetValue("INSTALLDIR", out var dir) && !string.IsNullOrWhiteSpace(dir)) InstallDir = dir.TrimEnd('\\');

        SelectedTasks = new HashSet<string>(manifest.Tasks.Where(t => t.Default).Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        if (args.Properties.TryGetValue("ADDLOCAL", out var addLocal))
        {
            SelectedTasks.Clear();
            foreach (var t in addLocal.Split(',', StringSplitOptions.RemoveEmptyEntries)) SelectedTasks.Add(t.Trim());
        }

        Prerequisites = manifest.Prerequisites.Select(p => Prerequisite.Create(p, manifest.Arch)).ToList();

        Log.Info($"Tenon setup {manifest.TenonVersion} for {manifest.ProductName} {manifest.Version} ({manifest.Arch})");
        Log.Info($"Command line: {Environment.CommandLine}");
        Log.Info($"Installed: {(Installed == null ? "none" : $"{Installed.VersionString} ({Installed.ProductCode}, {(Installed.PerMachine ? "per-machine" : "per-user")}) at {Installed.InstallLocation}")}");
        Log.Info($"Elevated process: {IsProcessElevated}");
    }

    public SetupManifest Manifest { get; }
    public SetupArgs Args { get; }
    public SetupLog Log { get; }
    public string TempDir { get; }
    public InstalledProduct? Installed { get; }
    public IReadOnlyList<Prerequisite> Prerequisites { get; }
    public string MsiLogPath { get; set; } = "";

    // ---- options chosen by the user (or defaults)
    public bool PerMachine { get; set; }
    public string InstallDir { get; set; }
    public HashSet<string> SelectedTasks { get; }
    public bool DeleteAppData { get; set; }

    public bool CanChooseScope => Manifest.Scope == "either" && Installed == null;
    public bool IsInstalled => Installed != null;
    public bool IsUpgrade => Installed != null && CompareVersions(Installed.VersionString, Manifest.MsiVersion) < 0;
    public bool IsSameVersion => Installed != null && CompareVersions(Installed.VersionString, Manifest.MsiVersion) == 0;
    public bool IsDowngrade => Installed != null && CompareVersions(Installed.VersionString, Manifest.MsiVersion) > 0;
    public bool CanDeleteAppData => Manifest.DeleteAppDataOnUninstall.Count > 0;

    /// <summary>Prerequisites that are missing on this machine (evaluated lazily, logged once).</summary>
    public IReadOnlyList<Prerequisite> MissingPrerequisites => _missing ??= Prerequisites.Where(p => !p.IsSatisfied(Log)).ToList();
    private List<Prerequisite>? _missing;

    public bool NeedsElevationForInstall => (PerMachine || MissingPrerequisites.Any(p => p.RequiresElevation)) && !IsProcessElevated;
    public bool NeedsElevationForUninstall => Installed?.PerMachine == true && !IsProcessElevated;

    public static bool IsProcessElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private static int CompareVersions(string a, string b)
    {
        Version.TryParse(a, out var va);
        Version.TryParse(b, out var vb);
        return (va ?? new Version(0, 0)).CompareTo(vb ?? new Version(0, 0));
    }

    // ------------------------------------------------------------------ payload

    public string ExtractMsi()
    {
        if (_extractedMsi != null && File.Exists(_extractedMsi)) return _extractedMsi;
        var name = SafeFileName($"{Manifest.ProductName}-{Manifest.Version}.msi");
        _extractedMsi = _payload.Extract(Manifest.MsiEntry, Path.Combine(TempDir, name));
        Log.Info($"Extracted package to {_extractedMsi}");
        return _extractedMsi;
    }

    public void UseExtractedMsi(string path) => _extractedMsi = path;
    public string ExtractEntry(string entry, string? fileName = null) => _payload.Extract(entry, Path.Combine(TempDir, fileName ?? entry));
    public bool HasEntry(string entry) => _payload.Contains(entry);
    public byte[] ReadEntry(string entry) => _payload.ReadBytes(entry);

    // ------------------------------------------------------------------ properties

    public string BuildInstallProperties()
    {
        var props = new List<string> { "TENON_SETUP=1", "ARPSYSTEMCOMPONENT=1" };
        if (Manifest.Scope == "either")
            props.Add(PerMachine ? "ALLUSERS=1 MSIINSTALLPERUSER=\"\"" : "ALLUSERS=2 MSIINSTALLPERUSER=1");
        if (!string.IsNullOrWhiteSpace(InstallDir)) props.Add($"INSTALLDIR=\"{InstallDir.TrimEnd('\\')}\\\"");
        if (Manifest.Tasks.Count > 0)
        {
            var selected = Manifest.Tasks.Where(t => SelectedTasks.Contains(t.Id)).Select(t => t.Id).ToList();
            var unselected = Manifest.Tasks.Where(t => !SelectedTasks.Contains(t.Id)).Select(t => t.Id).ToList();
            props.Add("ADDLOCAL=" + string.Join(",", new[] { "Main" }.Concat(selected)));
            if (unselected.Count > 0) props.Add("REMOVE=" + string.Join(",", unselected));
        }
        foreach (var kv in Args.Properties)
        {
            if (kv.Key is "ALLUSERS" or "INSTALLDIR" or "ADDLOCAL" or "MSIINSTALLPERUSER") continue;
            props.Add($"{kv.Key.ToUpperInvariant()}=\"{kv.Value}\"");
        }
        if (Args.NoRestart) props.Add("REBOOT=ReallySuppress");
        return string.Join(" ", props);
    }

    private ElevatedPlan MakePlan(string mode) => new()
    {
        Mode = mode,
        PerMachine = PerMachine,
        InstallDir = InstallDir,
        SelectedTasks = SelectedTasks.ToList(),
        Properties = new Dictionary<string, string>(Args.Properties),
        NoRestart = Args.NoRestart,
        MsiLogPath = MsiLogPath,
        ExtractedMsi = _extractedMsi,
        DeleteAppData = DeleteAppData,
    };

    // ------------------------------------------------------------------ operations

    public async Task<SetupResult> InstallAsync(IProgress<SetupProgress> progress, CancellationToken ct)
    {
        if (IsDowngrade)
            return new SetupResult { ExitCode = 1638, ErrorMessage = $"A newer version ({Installed!.VersionString}) is already installed.", Hint = "Uninstall it first to install this version." };

        if (NeedsElevationForInstall)
        {
            if (PerMachine)
            {
                // Everything runs elevated: prerequisites, MSI, registration.
                ExtractMsi();
                return await new ElevationClient(Log).RunAsync(MakePlan("install"), TempDir, progress, ct);
            }

            // Per-user install with machine-wide prerequisites: elevate only for those.
            var prereq = await new ElevationClient(Log).RunAsync(MakePlan("prereqs"), TempDir, progress, ct);
            if (!prereq.Succeeded) return prereq;
            _missing = null;
            var result = await RunMsiAsync(progress, ct, r => r.Install(ExtractMsi(), BuildInstallProperties(), MsiLogPath));
            result.RebootRequired |= prereq.RebootRequired;
            if (result.Succeeded) AfterInstall();
            return result;
        }

        var pre = await InstallPrerequisitesAsync(progress, ct);
        if (!pre.Succeeded) return pre;
        var install = await RunMsiAsync(progress, ct, r =>
        {
            var msi = ExtractMsi();
            var props = BuildInstallProperties();
            Log.Info($"MsiInstallProduct(\"{msi}\", \"{props}\")");
            return r.Install(msi, props, MsiLogPath);
        });
        install.RebootRequired |= pre.RebootRequired;
        if (install.Succeeded) AfterInstall();
        return install;
    }

    public async Task<SetupResult> UninstallAsync(IProgress<SetupProgress> progress, CancellationToken ct)
    {
        if (Installed == null) return new SetupResult { ExitCode = 0 };
        if (NeedsElevationForUninstall)
            return await new ElevationClient(Log).RunAsync(MakePlan("uninstall"), TempDir, progress, ct);

        var result = await RunMsiAsync(progress, ct, r =>
        {
            var props = "TENON_SETUP=1" + (Args.NoRestart ? " REBOOT=ReallySuppress" : "") + (DeleteAppData ? " TENON_DELETEAPPDATA=1" : "");
            foreach (var kv in Args.Properties) props += $" {kv.Key.ToUpperInvariant()}=\"{kv.Value}\"";
            Log.Info($"MsiConfigureProductEx(\"{Installed.ProductCode}\", ABSENT, \"{props}\")");
            return r.Uninstall(Installed.ProductCode, props, MsiLogPath);
        });
        if (result.Succeeded) AfterUninstall();
        return result;
    }

    public async Task<SetupResult> RepairAsync(IProgress<SetupProgress> progress, CancellationToken ct)
    {
        if (Installed == null) return new SetupResult { ExitCode = 1605, ErrorMessage = "The product is not installed." };
        if (NeedsElevationForUninstall)
            return await new ElevationClient(Log).RunAsync(MakePlan("repair"), TempDir, progress, ct);

        return await RunMsiAsync(progress, ct, r =>
        {
            var props = "TENON_SETUP=1" + (Args.NoRestart ? " REBOOT=ReallySuppress" : "");
            Log.Info($"Repair {Installed.ProductCode}");
            return r.Repair(Installed.ProductCode, props, MsiLogPath);
        });
    }

    public async Task<SetupResult> InstallPrerequisitesAsync(IProgress<SetupProgress> progress, CancellationToken ct)
    {
        var result = new SetupResult { ExitCode = 0 };
        foreach (var p in MissingPrerequisites)
        {
            if (p.Def.Requires == "perMachine" && !PerMachine)
            {
                Log.Info($"Skipping {p.DisplayName}: only needed for per-machine installs.");
                continue;
            }
            Log.Info($"Installing prerequisite {p.DisplayName}");
            uint rc;
            try
            {
                rc = await p.InstallAsync(this, progress, ct);
            }
            catch (OperationCanceledException)
            {
                return new SetupResult { ExitCode = 1602 };
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                return new SetupResult { ExitCode = 1603, ErrorMessage = $"{p.DisplayName} could not be installed: {ex.Message}" };
            }
            if (rc == 3010) result.RebootRequired = true;
            else if (rc != 0)
                return new SetupResult { ExitCode = (int)rc, ErrorMessage = $"{p.DisplayName} could not be installed (error {rc}).", Hint = "Install it manually and run Setup again." };
        }
        _missing = null;
        return result;
    }

    private Task<SetupResult> RunMsiAsync(IProgress<SetupProgress> progress, CancellationToken ct, Func<MsiRunner, uint> operation)
        => Task.Run(() => RunMsi(progress, ct, operation));

    private SetupResult RunMsi(IProgress<SetupProgress> progress, CancellationToken ct, Func<MsiRunner, uint> operation)
    {
        var runner = new MsiRunner(Log);
        string? lastError = null;
        var status = "Preparing";
        runner.Progress += p =>
        {
            if (p.Action != null) status = p.Action;
            progress.Report(new SetupProgress(p.Percent, status, p.Detail));
        };
        runner.ErrorMessage += m => lastError = m;
        using var reg = ct.Register(runner.Cancel);

        uint rc;
        try
        {
            rc = operation(runner);
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
            return new SetupResult { ExitCode = (int)MsiApi.ERROR_INSTALL_FAILURE, ErrorMessage = ex.Message };
        }

        var result = new SetupResult { ExitCode = (int)rc, RebootRequired = rc == MsiApi.ERROR_SUCCESS_REBOOT_REQUIRED || rc == MsiApi.ERROR_SUCCESS_REBOOT_INITIATED };
        if (!result.Succeeded && !result.Cancelled)
        {
            result.ErrorMessage = lastError ?? DescribeExitCode(rc);
            result.Hint = HintFor(rc, lastError);
        }
        return result;
    }

    private void AfterInstall()
    {
        try
        {
            ArpRegistrar.Register(Manifest, PerMachine, InstallDir, Log);
        }
        catch (Exception ex)
        {
            Log.Warn("Add/Remove Programs registration failed: " + ex.Message);
        }
    }

    private void AfterUninstall()
    {
        // User data folders are removed by the package's own TenonDeleteAppData action (TENON_DELETEAPPDATA=1).
        ArpRegistrar.Unregister(Manifest, Installed?.PerMachine ?? PerMachine, Log);
    }

    // ------------------------------------------------------------------ helpers

    public static string DescribeExitCode(uint rc) => rc switch
    {
        MsiApi.ERROR_INSTALL_FAILURE => "The installation failed. See the log for details.",
        MsiApi.ERROR_INSTALL_ALREADY_RUNNING => "Another installation is already in progress. Wait for it to finish and try again.",
        MsiApi.ERROR_PRODUCT_VERSION => "A newer version of this product is already installed.",
        MsiApi.ERROR_INSTALL_USEREXIT => "The installation was cancelled.",
        1925 => "Administrator rights are required for this installation.",
        1633 => "This package is not supported on this processor architecture.",
        2 => "The installation package could not be found.",
        5 => "Access was denied.",
        _ => $"The installation failed with error {rc}.",
    };

    private static string? HintFor(uint rc, string? message)
    {
        if (rc == MsiApi.ERROR_INSTALL_ALREADY_RUNNING) return "Check Task Manager for msiexec.exe or a pending Windows Update.";
        if (rc == MsiApi.ERROR_INSTALL_FAILURE) return "Antivirus software or a locked file often causes this. Close the application and try again.";
        if (rc == 1925 || rc == 5) return "Right-click Setup and choose 'Run as administrator', or install for the current user only.";
        return null;
    }

    public static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    /// <summary>After an update started by Tenon.Update: relaunch the application that asked for it.</summary>
    public void RestartAppIfRequested()
    {
        var app = Args.RestartApp;
        if (string.IsNullOrEmpty(app)) return;
        if (!File.Exists(app))
        {
            // The executable may have moved; fall back to the installed main executable.
            app = string.IsNullOrEmpty(Manifest.MainExe) ? null : Path.Combine(InstallDir, Manifest.MainExe);
            if (app == null || !File.Exists(app)) return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(app, Args.RestartArgs ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app) });
            Log.Info($"Restarted {app}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot restart {app}: {ex.Message}");
        }
    }

    public void LaunchApp()
    {
        if (string.IsNullOrEmpty(Manifest.MainExe)) return;
        var exe = Path.Combine(InstallDir, Manifest.MainExe);
        if (!File.Exists(exe)) { Log.Warn($"Cannot launch {exe}: file not found."); return; }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            Log.Info($"Launched {exe}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot launch {exe}: {ex.Message}");
        }
    }

    /// <summary>
    /// When Setup runs from the package cache and is about to remove it, re-launch a temp copy so the
    /// cache folder can be deleted. Returns true when the current process should exit.
    /// </summary>
    public bool RelaunchFromTempIfNeeded()
    {
        if (Args.Elevated || Args.Mode != SetupMode.Uninstall || !ArpRegistrar.IsRunningFromCache(Manifest)) return false;
        try
        {
            var copy = Path.Combine(Path.GetTempPath(), "Tenon", SafeFileName(Manifest.ProductName) + "-Uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(Environment.ProcessPath!, copy, overwrite: true);
            var args = Environment.GetCommandLineArgs().Skip(1).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a);
            Process.Start(new ProcessStartInfo(copy, string.Join(" ", args)) { UseShellExecute = true });
            Log.Info($"Relaunched from {copy} so the package cache can be removed.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not relaunch from temp: " + ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _payload.Dispose();
            if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true);
            for (var dir = Directory.GetParent(TempDir); dir != null && dir.Name != Path.GetFileName(Path.GetTempPath().TrimEnd('\\')); dir = dir.Parent)
            {
                if (dir.Exists && !dir.EnumerateFileSystemInfos().Any()) dir.Delete(); else break;
            }
        }
        catch
        {
            // best effort
        }
    }
}
