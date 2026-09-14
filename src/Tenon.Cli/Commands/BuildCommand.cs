using System.Diagnostics;
using Tenon.Cli.Pipeline;
using Tenon.Core.Loading;
using Tenon.Core.Logging;
using Tenon.Msi;

namespace Tenon.Cli.Commands;

public sealed class BuildCommand
{
    private readonly ITenonLogger _log;

    public BuildCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var sw = Stopwatch.StartNew();
        var path = DefinitionLoader.Locate(cl.Option("definition", "d"));
        var doc = DefinitionLoader.Load(path);
        var ctx = new BuildContext(doc, _log);
        _log.Info($"Using {Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}");

        // 1. Publish (or reuse a publish folder)
        var publishDir = cl.Option("publish-dir") ?? ctx.Definition.Build.PublishDir;
        if (publishDir != null)
        {
            ctx.PublishDir = doc.Resolve(publishDir);
            if (!Directory.Exists(ctx.PublishDir)) throw new DirectoryNotFoundException($"Publish folder '{ctx.PublishDir}' does not exist.");
            _log.Info($"Using publish folder {ctx.PublishDir}");
        }
        else if (cl.Flag("no-publish"))
        {
            throw new InvalidOperationException("--no-publish requires build.publishDir in tenon.json or --publish-dir.");
        }
        else
        {
            ctx.PublishDir = ctx.Publish(cl.Option("config", "c"));
        }

        // 2. Macros, harvesting, planning
        ctx.ExpandMacros();
        ctx.ResolveDefinitionPaths();
        ctx.Harvest();
        ctx.Lint.ThrowIfErrors();

        var def = ctx.Definition;
        ctx.OutputDir = doc.Resolve(cl.Option("out", "o") ?? def.Output.Dir);
        Directory.CreateDirectory(ctx.OutputDir);

        var msiName = def.Output.Msi;
        if (!msiName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) msiName += ".msi";
        var msiPath = Path.Combine(ctx.OutputDir, SanitizeFileName(msiName));

        var builder = new MsiBuilder(def, ctx.Files, ctx.Lint, _log, new MsiBuildOptions
        {
            IntermediateDir = Path.Combine(ctx.IntermediateDir, "cab"),
            Planner = new Tenon.Msi.Planning.PlannerOptions { CustomActions = ctx.PrepareCustomActions(cl.Option("config", "c")) },
        });

        var result = builder.Build(msiPath);
        ctx.ReportLint();

        var signer = new Signing.Signer(def.Signing, _log);
        if (signer.Enabled && !cl.Flag("no-sign")) signer.Sign(result.MsiPath);

        _log.Info("");
        _log.Info($"Built {Path.GetRelativePath(Directory.GetCurrentDirectory(), result.MsiPath)}");
        _log.Info($"  product      {result.Plan.ProductName} {result.Plan.DisplayVersion} ({result.Plan.ArchName}, {result.Plan.Scope.ToString().ToLowerInvariant()})");
        _log.Info($"  files        {result.FileCount}");
        _log.Info($"  size         {new FileInfo(result.MsiPath).Length / 1024.0 / 1024.0:F1} MB");
        _log.Info($"  product code {result.ProductCode}");
        _log.Info($"  upgrade code {result.Plan.UpgradeCode}");
        _log.Info($"  time         {sw.Elapsed.TotalSeconds:F1}s");

        if (!cl.Flag("msi-only"))
        {
            var setupName = def.Output.Setup;
            if (!setupName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) setupName += ".exe";
            var setupPath = Path.Combine(ctx.OutputDir, SanitizeFileName(setupName));
            new SetupExeBuilder(ctx, _log).Build(result.MsiPath, result.Plan, setupPath);
            if (signer.Enabled && !cl.Flag("no-sign")) signer.Sign(setupPath);
            _log.Info($"Built {Path.GetRelativePath(Directory.GetCurrentDirectory(), setupPath)} ({new FileInfo(setupPath).Length / 1024.0 / 1024.0:F1} MB)");
            if (!def.Output.ExtraMsi) File.Delete(result.MsiPath);
        }

        return 0;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }
}
