using Tenon.Core.Logging;
using Tenon.Core.Model;
using Tenon.Msi.Planning;
using Tenon.Payload;

namespace Tenon.Cli.Pipeline;

/// <summary>Produces Setup.exe: the prebuilt WPF stub with the manifest, MSI and assets appended as a payload.</summary>
public sealed class SetupExeBuilder
{
    private readonly BuildContext _ctx;
    private readonly ITenonLogger _log;

    public SetupExeBuilder(BuildContext ctx, ITenonLogger log)
    {
        _ctx = ctx;
        _log = log;
    }

    public string Build(string msiPath, InstallPlan plan, string outputPath)
    {
        var def = _ctx.Definition;
        var stub = FindStub(plan.ArchName) ?? throw new FileNotFoundException(
            $"The setup stub for {plan.ArchName} was not found. Set TENON_STUB to the path of tenon-setup.exe, " +
            "or publish it with: dotnet publish src/Tenon.Setup -r win-" + plan.ArchName);
        _log.Debug($"Using setup stub {stub}");

        var manifest = CreateManifest(def, plan);
        var writer = new PayloadWriter();

        writer.AddFile(manifest.MsiEntry, msiPath);
        if (!string.IsNullOrEmpty(def.Ui.License))
        {
            var license = _ctx.Document.Resolve(def.Ui.License!);
            if (!File.Exists(license)) throw new FileNotFoundException($"ui.license '{license}' does not exist.");
            manifest.Ui.LicenseEntry = "license" + Path.GetExtension(license).ToLowerInvariant();
            writer.AddFile(manifest.Ui.LicenseEntry, license);
        }
        if (!string.IsNullOrEmpty(def.Ui.Theme.Logo))
        {
            var logo = _ctx.Document.Resolve(def.Ui.Theme.Logo!);
            if (!File.Exists(logo)) throw new FileNotFoundException($"ui.theme.logo '{logo}' does not exist.");
            manifest.Ui.LogoEntry = "logo" + Path.GetExtension(logo).ToLowerInvariant();
            writer.AddFile(manifest.Ui.LogoEntry, logo);
        }
        if (!string.IsNullOrEmpty(def.Product.Icon))
        {
            var icon = _ctx.Document.Resolve(def.Product.Icon!);
            if (!File.Exists(icon)) throw new FileNotFoundException($"product.icon '{icon}' does not exist.");
            manifest.IconEntry = "product.ico";
            writer.AddFile(manifest.IconEntry, icon);
            // The Setup.exe file itself gets the product icon instead of the Tenon icon.
            writer.PrepareStub = stubCopy =>
            {
                try
                {
                    Pe.IconResourceUpdater.Apply(stubCopy, icon);
                    _log.Debug("Applied product icon to Setup.exe");
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not apply the product icon to Setup.exe: {ex.Message}");
                }
            };
        }
        // Prerequisites: resolve, download to the local cache and embed unless marked download-only.
        manifest.Prerequisites.Clear();
        if (def.Prerequisites.Count > 0)
        {
            var resolver = new Prereqs.PrereqResolver(_log);
            foreach (var p in def.Prerequisites)
            {
                var resolved = resolver.ResolveAsync(p, plan.ArchName, _ctx.Document.Resolve).GetAwaiter().GetResult();
                manifest.Prerequisites.Add(resolved.Manifest);
                if (resolved.LocalPath != null && resolved.Manifest.Entry != null)
                    writer.AddFile(resolved.Manifest.Entry, resolved.LocalPath);
            }
        }

        writer.AddText(PayloadFormat.ManifestEntry, manifest.ToJson());

        _log.Info($"Writing {Path.GetFileName(outputPath)} ...");
        writer.Write(stub, outputPath);
        return outputPath;
    }

    public static SetupManifest CreateManifest(InstallerDefinition def, InstallPlan plan)
    {
        var m = new SetupManifest
        {
            TenonVersion = typeof(SetupExeBuilder).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            ProductName = plan.ProductName,
            Version = plan.DisplayVersion,
            MsiVersion = plan.MsiVersion,
            Publisher = plan.Manufacturer,
            UpgradeCode = plan.UpgradeCode,
            ProductCode = plan.ProductCode,
            Description = plan.Description,
            Url = plan.Url,
            SupportUrl = plan.SupportUrl,
            Arch = plan.ArchName,
            Languages = def.Product.Languages.ToList(),
            Scope = def.Install.Scope switch { InstallScope.PerMachine => "perMachine", InstallScope.PerUser => "perUser", _ => "either" },
            DefaultScope = def.Install.DefaultScope == InstallScope.PerMachine ? "perMachine" : "perUser",
            InstallDir = def.Install.Dir,
            AllowChangeDir = def.Install.AllowChangeDir,
            MainExe = def.Install.MainExe,
            RunAfterInstall = def.Install.RunAfterInstall,
            CloseRunningApp = def.Install.CloseRunningApp.ToString().ToLowerInvariant(),
            MinWindowsBuild = plan.MinWindowsBuild,
            MinWindowsDisplay = plan.MinWindowsDisplay,
            ArpNoModify = def.Install.Arp.NoModify,
            ArpNoRepair = def.Install.Arp.NoRepair,
            DeleteAppDataOnUninstall = def.Install.DeleteAppDataOnUninstall.ToList(),
            MsiEntry = "package.msi",
        };
        foreach (var t in def.Tasks)
            m.Tasks.Add(new ManifestTask { Id = t.Id, Title = t.Title, Description = t.Description, Default = t.Default });
        m.Ui = new ManifestUi
        {
            Style = def.Ui.Style == UiStyle.OneClick ? "oneClick" : "modern",
            Pages = def.Ui.Pages.ToList(),
            Accent = def.Ui.Theme.Accent,
            Background = def.Ui.Theme.Background,
            DarkMode = def.Ui.Theme.DarkMode,
            Font = def.Ui.Theme.Font,
        };
        return m;
    }

    /// <summary>Locates tenon-setup.exe: TENON_STUB, the tool package, or the development tree.</summary>
    public static string? FindStub(string arch)
    {
        var env = Environment.GetEnvironmentVariable("TENON_STUB");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        var baseDir = AppContext.BaseDirectory;
        var packaged = Path.Combine(baseDir, "stub", "win-" + arch, "tenon-setup.exe");
        if (File.Exists(packaged)) return packaged;

        for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Tenon.sln"))) continue;
            var dev = Path.Combine(dir.FullName, "src", "Tenon.Setup", "bin", "stub", "win-" + arch, "tenon-setup.exe");
            if (File.Exists(dev)) return dev;
            break;
        }
        return null;
    }
}
