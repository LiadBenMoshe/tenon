using System.Xml.Linq;
using Tenon.Core.Loading;
using Tenon.Core.Logging;
using Tenon.Core.Model;

namespace Tenon.Cli.Commands;

public sealed class InitCommand
{
    private readonly ITenonLogger _log;

    public InitCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var cwd = Directory.GetCurrentDirectory();
        var target = Path.Combine(cwd, "tenon.json");
        if (File.Exists(target) && !cl.Flag("force"))
        {
            _log.Error($"{target} already exists. Use --force to overwrite.");
            return 1;
        }

        var projectArg = cl.Option("project", "p");
        string? projectPath = null;
        if (projectArg != null)
        {
            projectPath = Path.GetFullPath(projectArg);
        }
        else
        {
            var candidates = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .OrderBy(p => p.Length)
                .ToList();
            projectPath = candidates.FirstOrDefault(IsExeProject) ?? candidates.FirstOrDefault();
            if (projectPath != null) _log.Info($"Using project {Path.GetRelativePath(cwd, projectPath)}");
        }

        var assemblyName = projectPath != null ? ReadAssemblyName(projectPath) : "MyApp";
        var productName = cl.Option("name") ?? assemblyName;
        var publisher = cl.Option("publisher") ?? "My Company";

        var def = new InstallerDefinition
        {
            Schema = "https://tenon.dev/schema/v1/tenon.schema.json",
            Build = new BuildSection { Project = projectPath != null ? Path.GetRelativePath(cwd, projectPath) : "MyApp.csproj" },
            Product = new ProductSection
            {
                Name = productName,
                Version = "$(AssemblyVersion)",
                Publisher = publisher,
                UpgradeCode = Guid.NewGuid().ToString().ToUpperInvariant(),
            },
            Install = new InstallSection
            {
                Scope = InstallScope.Either,
                Dir = "{autopf}\\" + productName,
                MainExe = assemblyName + ".exe",
            },
            Files = { new FileRule { From = "{publish}\\**", Exclude = { "*.pdb" }, To = "{app}" } },
            Shortcuts =
            {
                new ShortcutDef { Name = productName, Target = "{app}\\" + assemblyName + ".exe", In = "{group}" },
                new ShortcutDef { Name = productName, Target = "{app}\\" + assemblyName + ".exe", In = "{desktop}", Task = "desktopIcon" },
            },
            Tasks = { new TaskDef { Id = "desktopIcon", Title = "Create a desktop shortcut", Default = true } },
            Prerequisites = { new PrerequisiteDef { Id = "dotnet-desktop", Version = "8.0" } },
        };

        DefinitionLoader.Save(def, target);
        _log.Info($"Created {target}");
        _log.Info("Next: review product.publisher and install.dir, then run 'tenon build'.");
        return 0;
    }

    private static bool IsExeProject(string csproj)
    {
        try
        {
            var doc = XDocument.Load(csproj);
            return doc.Descendants("OutputType").Any(e => e.Value.Contains("Exe", StringComparison.OrdinalIgnoreCase))
                   || doc.Descendants("UseWPF").Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                   || doc.Descendants("UseWindowsForms").Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string ReadAssemblyName(string csproj)
    {
        try
        {
            var doc = XDocument.Load(csproj);
            var name = doc.Descendants("AssemblyName").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name!;
        }
        catch
        {
            // fall back to file name
        }
        return Path.GetFileNameWithoutExtension(csproj);
    }
}
