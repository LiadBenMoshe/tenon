using System.Diagnostics;
using Tenon.Core.Files;
using Tenon.Core.Loading;
using Tenon.Core.Logging;
using Tenon.Core.Model;
using Tenon.Core.Validation;

namespace Tenon.Cli.Pipeline;

/// <summary>Everything the build steps share: the loaded definition, resolved folders, macro values and lint results.</summary>
public sealed class BuildContext
{
    public BuildContext(DefinitionDocument document, ITenonLogger log)
    {
        Document = document;
        Log = log;
    }

    public DefinitionDocument Document { get; }
    public InstallerDefinition Definition => Document.Definition;
    public ITenonLogger Log { get; }
    public LintCollector Lint { get; } = new();
    public Dictionary<string, string> Props { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PublishDir { get; set; }
    public string IntermediateDir => Path.Combine(Document.Directory, ".tenon");
    public string OutputDir { get; set; } = "";
    public IReadOnlyList<HarvestedFile> Files { get; set; } = Array.Empty<HarvestedFile>();

    public string Rid => Definition.Build.RuntimeIdentifier ?? Definition.Product.Arch switch
    {
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64",
    };

    /// <summary>Runs dotnet publish for build.project into the intermediate folder.</summary>
    public string Publish(string? configuration)
    {
        var project = Definition.Build.Project;
        if (string.IsNullOrEmpty(project))
            throw new InvalidOperationException("build.project is not set and no publish folder was given. Set build.project in tenon.json or pass --publish-dir.");

        var projectPath = Document.Resolve(project!);
        if (!File.Exists(projectPath)) throw new FileNotFoundException($"build.project '{projectPath}' does not exist.");

        var config = configuration ?? Definition.Build.Configuration;
        var outDir = Path.Combine(IntermediateDir, "publish", Rid);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        var args = $"publish \"{projectPath}\" -c {config} -r {Rid} --self-contained {(Definition.Build.SelfContained ? "true" : "false")} -o \"{outDir}\" -nologo";
        if (!string.IsNullOrEmpty(Definition.Build.PublishArgs)) args += " " + Definition.Build.PublishArgs;

        Log.Info($"Publishing {Path.GetFileName(projectPath)} ({config}, {Rid}) ...");
        var exit = RunDotnet(args, Log);
        if (exit != 0) throw new InvalidOperationException($"dotnet publish failed with exit code {exit}.");
        return outDir;
    }

    public static string DotnetPath
    {
        get
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(host) && File.Exists(host)) return host;
            var process = Environment.ProcessPath;
            if (process != null && Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return process;
            return "dotnet";
        }
    }

    public static int RunDotnet(string arguments, ITenonLogger log)
    {
        var psi = new ProcessStartInfo(DotnetPath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) log.Debug(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) log.Warn(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>Fills the $(...) macro values and expands them inside the definition.</summary>
    public void ExpandMacros()
    {
        var def = Definition;
        Props["Arch"] = def.Product.Arch.ToString().ToLowerInvariant();
        Props["Configuration"] = def.Build.Configuration;
        Props["RuntimeIdentifier"] = Rid;
        Props["DefinitionDir"] = Document.Directory;
        if (PublishDir != null) Props["PublishDir"] = PublishDir;

        var assemblyVersion = ReadAssemblyVersion();
        if (assemblyVersion != null)
        {
            Props["AssemblyVersion"] = assemblyVersion.Value.file;
            Props["AssemblyInformationalVersion"] = assemblyVersion.Value.product;
            Props["FileVersion"] = assemblyVersion.Value.file;
        }

        // Environment variables are available as $(env.NAME).
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            Props["env." + e.Key] = e.Value?.ToString() ?? "";

        // First pass resolves product fields, second pass lets other fields reference them.
        MacroExpander.ExpandInPlace(def.Product, Props);
        Props["ProductName"] = def.Product.Name;
        Props["Publisher"] = def.Product.Publisher;
        Props["Version"] = def.Product.Version;

        var unknown = MacroExpander.ExpandInPlace(def, Props);
        foreach (var u in unknown)
        {
            if (u.Equals("AssemblyVersion", StringComparison.OrdinalIgnoreCase) && assemblyVersion == null)
                Lint.Error(LintIds.UnknownMacro, "$(AssemblyVersion) could not be resolved because no executable was found in the publish output.",
                    "Set install.mainExe, or set product.version explicitly.", "product.version");
            else
                Lint.Error(LintIds.UnknownMacro, $"Unknown macro $({u}).",
                    "Available macros: " + string.Join(", ", Props.Keys.Where(k => !k.StartsWith("env.")).OrderBy(k => k).Select(k => "$(" + k + ")")) + ", $(env.NAME)");
        }
    }

    private (string file, string product)? ReadAssemblyVersion()
    {
        if (PublishDir == null || !Directory.Exists(PublishDir)) return null;
        string? exe = null;
        if (!string.IsNullOrEmpty(Definition.Install.MainExe))
        {
            var candidate = Path.Combine(PublishDir, Definition.Install.MainExe!);
            if (File.Exists(candidate)) exe = candidate;
        }
        exe ??= Directory.EnumerateFiles(PublishDir, "*.exe").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (exe == null) return null;

        var fvi = FileVersionInfo.GetVersionInfo(exe);
        var file = $"{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}" + (fvi.FilePrivatePart != 0 ? "." + fvi.FilePrivatePart : "");
        var product = fvi.ProductVersion ?? file;
        var plus = product.IndexOf('+');
        if (plus > 0) product = product.Substring(0, plus);
        return (file, product);
    }

    /// <summary>Locates the native custom action DLL and hook host, and builds/packs the hooks project if any.</summary>
    public Tenon.Msi.Planning.CustomActionInputs PrepareCustomActions(string? configuration)
    {
        var arch = Definition.Product.Arch.ToString().ToLowerInvariant();
        var inputs = new Tenon.Msi.Planning.CustomActionInputs
        {
            NativeDllPath = ToolAssets.Find(arch, "TenonCA.dll"),
            HookHostPath = ToolAssets.Find(arch, "tenon-hookhost.exe"),
            HookRuntimeFromApp = Definition.Hooks?.Runtime.Equals("app", StringComparison.OrdinalIgnoreCase) == true,
        };
        if (inputs.NativeDllPath == null) Log.Debug($"TenonCA.dll for {arch} not found; custom actions are unavailable.");

        var hooks = Definition.Hooks;
        if (hooks == null || (string.IsNullOrEmpty(hooks.Project) && string.IsNullOrEmpty(hooks.Assembly))) return inputs;

        string binDir;
        string? projectName = null;
        if (!string.IsNullOrEmpty(hooks.Assembly))
        {
            var asm = Document.Resolve(hooks.Assembly!);
            if (!File.Exists(asm)) throw new FileNotFoundException($"hooks.assembly '{asm}' does not exist.");
            binDir = Path.GetDirectoryName(asm)!;
            projectName = Path.GetFileNameWithoutExtension(asm);
        }
        else
        {
            var project = Document.Resolve(hooks.Project!);
            if (!File.Exists(project)) throw new FileNotFoundException($"hooks.project '{project}' does not exist.");
            projectName = Path.GetFileNameWithoutExtension(project);
            binDir = Path.Combine(IntermediateDir, "hooks", "bin");
            if (Directory.Exists(binDir)) Directory.Delete(binDir, recursive: true);
            Log.Info($"Building hooks {Path.GetFileName(project)} ...");
            var exit = RunDotnet($"build \"{project}\" -c {configuration ?? Definition.Build.Configuration} -o \"{binDir}\" -nologo -v q", Log);
            if (exit != 0) throw new InvalidOperationException($"Building the hooks project failed with exit code {exit}.");
        }

        var assemblyPath = Tenon.Msi.Hooks.HooksPackager.FindHooksAssembly(binDir, projectName);
        inputs.Hooks = Tenon.Msi.Hooks.HookManifest.Scan(assemblyPath);
        inputs.HooksZipPath = Tenon.Msi.Hooks.HooksPackager.Pack(binDir, Path.Combine(IntermediateDir, "hooks", "hooks.zip"));
        var size = new FileInfo(inputs.HooksZipPath).Length;
        Log.Info($"Hooks: {inputs.Hooks.AssemblyName} with stages {string.Join(", ", inputs.Hooks.Stages.Keys)} ({size / 1024} KB)");
        if (size > Tenon.Msi.Hooks.HooksPackager.WarnAboveBytes)
            Lint.Warning(Core.Validation.LintIds.HookRuntime, $"The hooks package is {size / 1024 / 1024} MB. Keep hooks small; large dependencies belong in 'files'.", null, "hooks");
        if (inputs.Hooks.Stages.Count == 0)
            Lint.Warning(Core.Validation.LintIds.HookRuntime, $"No [SetupHook] methods were found in {inputs.Hooks.AssemblyName}. Classes must implement ISetupHooks.", null, "hooks");
        return inputs;
    }

    public void Harvest()
    {
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src"] = Document.Directory,
        };
        if (PublishDir != null) constants["publish"] = PublishDir;
        var harvester = new GlobHarvester(Document.Directory, constants, Lint);
        Files = harvester.Harvest(Definition.Files);
    }

    public void ReportLint(bool includeInfo = false)
    {
        foreach (var m in Lint.Messages)
        {
            switch (m.Severity)
            {
                case LintSeverity.Error: Log.Error(m.ToString()); break;
                case LintSeverity.Warning: Log.Warn(m.ToString()); break;
                default: if (includeInfo) Log.Info(m.ToString()); break;
            }
        }
    }
}
