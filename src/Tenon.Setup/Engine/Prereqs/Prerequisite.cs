using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;
using Tenon.Native;
using Tenon.Payload;

namespace Tenon.Setup.Engine.Prereqs;

/// <summary>A runtime or package that must be present before the product's MSI runs.</summary>
public abstract class Prerequisite
{
    protected Prerequisite(ManifestPrerequisite def, string arch)
    {
        Def = def;
        Arch = arch;
    }

    public ManifestPrerequisite Def { get; }
    public string Arch { get; }
    public abstract string DisplayName { get; }
    public virtual bool RequiresElevation => Def.Requires != "perUser";
    public abstract bool IsSatisfied(SetupLog log);

    /// <summary>Default install: run the package (exe or msi) silently and map the exit code.</summary>
    public virtual async Task<uint> InstallAsync(SetupEngine engine, IProgress<SetupProgress> progress, CancellationToken ct)
    {
        var log = engine.Log;
        progress.Report(new SetupProgress(0, $"Installing {DisplayName}", null));
        var package = await AcquirePackageAsync(engine, progress, ct);

        if (package.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            var runner = new MsiRunner(log);
            runner.Progress += p => progress.Report(new SetupProgress(0, $"Installing {DisplayName}", p.Detail));
            var msiLog = Path.ChangeExtension(engine.MsiLogPath, null) + "_" + SetupEngine.SafeFileName(Def.Id) + ".log";
            var rc = runner.Install(package, "REBOOT=ReallySuppress " + (Def.Args ?? ""), msiLog);
            return rc;
        }

        var args = Def.Args ?? "/install /quiet /norestart";
        log.Info($"Running prerequisite: \"{package}\" {args}");
        using var process = Process.Start(new ProcessStartInfo(package, args) { UseShellExecute = false, CreateNoWindow = true })
                            ?? throw new InvalidOperationException($"Could not start {package}");
        using var reg = ct.Register(() => { try { process.Kill(); } catch { /* ignore */ } });
        await process.WaitForExitAsync(CancellationToken.None);
        var exit = (uint)process.ExitCode;
        log.Info($"{DisplayName} installer returned {exit}");
        return exit switch
        {
            1638 => 0,          // a newer version is already installed
            1641 => 3010,       // reboot initiated -> treat as reboot required
            _ => exit,
        };
    }

    /// <summary>Extracts the embedded package or downloads it, verifying the SHA-256 in both cases.</summary>
    protected async Task<string> AcquirePackageAsync(SetupEngine engine, IProgress<SetupProgress> progress, CancellationToken ct)
    {
        var log = engine.Log;
        var fileName = Def.Url != null ? Path.GetFileName(new Uri(Def.Url).LocalPath) : (Def.Entry ?? Def.Id + ".exe");
        var target = Path.Combine(engine.TempDir, fileName);

        if (Def.Entry != null && engine.HasEntry(Def.Entry))
        {
            engine.ExtractEntry(Def.Entry, fileName);
            log.Info($"Extracted {DisplayName} package to {target}");
            return target;
        }

        if (Def.Url == null) throw new InvalidOperationException($"{DisplayName}: no package embedded and no download URL.");
        log.Info($"Downloading {Def.Url}");
        progress.Report(new SetupProgress(0, $"Downloading {DisplayName}", null));
        using var http = new HttpClient();
        using var response = await http.GetAsync(Def.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;
        await using (var stream = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1 << 16];
            long read = 0;
            int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress.Report(new SetupProgress((int)(read * 100 / total), $"Downloading {DisplayName}", $"{read / 1024 / 1024} of {total / 1024 / 1024} MB"));
            }
        }

        if (!string.IsNullOrEmpty(Def.Sha256))
        {
            await using var check = File.OpenRead(target);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
            if (!string.Equals(hash, Def.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The downloaded {DisplayName} package is corrupt (hash mismatch).");
        }
        return target;
    }

    public static Prerequisite Create(ManifestPrerequisite def, string arch) => def.Id.ToLowerInvariant() switch
    {
        "dotnet-desktop" => new DotNetRuntimePrerequisite(def, arch, desktop: true),
        "dotnet-runtime" => new DotNetRuntimePrerequisite(def, arch, desktop: false),
        "vcredist-x64" => new VcRedistPrerequisite(def, "x64"),
        "vcredist-x86" => new VcRedistPrerequisite(def, "x86"),
        "vcredist-arm64" => new VcRedistPrerequisite(def, "arm64"),
        _ => new CustomPrerequisite(def, arch),
    };
}

public sealed class DotNetRuntimePrerequisite : Prerequisite
{
    private readonly bool _desktop;

    public DotNetRuntimePrerequisite(ManifestPrerequisite def, string arch, bool desktop) : base(def, arch)
    {
        _desktop = desktop;
    }

    public override string DisplayName => $".NET {(_desktop ? "Desktop " : "")}Runtime {Def.Version}";

    private Version Requested => Version.TryParse(Def.Version, out var v) ? v : new Version(8, 0);

    public override bool IsSatisfied(SetupLog log)
    {
        var requested = Requested;
        var installed = InstalledVersions().ToList();
        log.Info($"{DisplayName}: installed versions {(installed.Count == 0 ? "none" : string.Join(", ", installed))}");
        var rollForward = (Def.RollForward ?? "latestMinor").ToLowerInvariant();
        return installed.Any(v => rollForward switch
        {
            "latestpatch" => v.Major == requested.Major && v.Minor == requested.Minor && v >= requested,
            "latestmajor" or "major" => v >= requested,
            _ => v.Major == requested.Major && v >= requested,
        });
    }

    private IEnumerable<Version> InstalledVersions()
    {
        var framework = _desktop ? "Microsoft.WindowsDesktop.App" : "Microsoft.NETCore.App";
        foreach (var root in DotnetRoots())
        {
            var dir = Path.Combine(root, "shared", framework);
            if (!Directory.Exists(dir)) continue;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                var dash = name.IndexOf('-');
                if (dash > 0) name = name.Substring(0, dash); // ignore previews
                if (Version.TryParse(name, out var v) && Directory.EnumerateFiles(sub).Any()) yield return v;
            }
        }
    }

    private IEnumerable<string> DotnetRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? pf = Arch == "x86" && Environment.Is64BitOperatingSystem
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            : Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (pf != null && seen.Add(Path.Combine(pf, "dotnet"))) yield return Path.Combine(pf, "dotnet");

        string? registered = null;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                .OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\{Arch}");
            registered = key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            // ignore
        }
        if (registered != null && seen.Add(registered)) yield return registered;
    }
}

public sealed class VcRedistPrerequisite : Prerequisite
{
    public VcRedistPrerequisite(ManifestPrerequisite def, string arch) : base(def, arch) { }

    public override string DisplayName => $"Microsoft Visual C++ Redistributable ({Arch})";

    public override bool IsSatisfied(SetupLog log)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{Arch}")
                ?? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                .OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{Arch}");
            if (key == null || key.GetValue("Installed") is not int installed || installed != 1) return false;
            var major = key.GetValue("Major") as int? ?? 0;
            var minor = key.GetValue("Minor") as int? ?? 0;
            var bld = key.GetValue("Bld") as int? ?? 0;
            var have = new Version(major, minor, bld);
            log.Info($"{DisplayName}: installed {have}");
            if (string.IsNullOrEmpty(Def.Version)) return true;
            return Version.TryParse(Def.Version, out var min) ? have >= min : true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class CustomPrerequisite : Prerequisite
{
    public CustomPrerequisite(ManifestPrerequisite def, string arch) : base(def, arch) { }

    public override string DisplayName => Def.DisplayName ?? Def.Id;

    public override bool IsSatisfied(SetupLog log)
    {
        var d = Def.Detect;
        if (d == null) return false;
        if (d.ProductCode != null) return MsiApi.MsiQueryProductStateW(d.ProductCode) is MsiApi.INSTALLSTATE_DEFAULT or MsiApi.INSTALLSTATE_LOCAL;
        if (d.UpgradeCode != null) return InstalledProduct.FindByUpgradeCode(d.UpgradeCode).Any(p => d.MinVersion == null || (Version.TryParse(d.MinVersion, out var min) && p.Version >= min));
        if (d.File != null) return File.Exists(Environment.ExpandEnvironmentVariables(d.File));
        if (d.RegistryValue != null)
        {
            var value = ReadRegistryValue(d.RegistryValue);
            if (value == null) return false;
            if (d.MinVersion == null) return true;
            return Version.TryParse(value, out var have) && Version.TryParse(d.MinVersion, out var min) && have >= min;
        }
        return false;
    }

    private static string? ReadRegistryValue(string path)
    {
        var parts = path.Split('\\');
        if (parts.Length < 3) return null;
        var hive = parts[0].ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => RegistryHive.LocalMachine,
        };
        var key = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
        var name = parts[parts.Length - 1];
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(key);
                if (k?.GetValue(name) is object v) return v.ToString();
            }
            catch
            {
                // ignore
            }
        }
        return null;
    }
}
