using Tenon.Cli.Pipeline;
using Tenon.Core.Loading;
using Tenon.Core.Logging;
using Tenon.Core.Validation;
using Tenon.Msi;

namespace Tenon.Cli.Commands;

public sealed class ValidateCommand
{
    private readonly ITenonLogger _log;

    public ValidateCommand(ITenonLogger log)
    {
        _log = log;
    }

    public int Run(CommandLine cl)
    {
        var path = DefinitionLoader.Locate(cl.Option("definition", "d"));
        var doc = DefinitionLoader.Load(path);
        var ctx = new BuildContext(doc, _log);

        var publishDir = cl.Option("publish-dir") ?? ctx.Definition.Build.PublishDir;
        if (publishDir != null) ctx.PublishDir = doc.Resolve(publishDir);
        else
        {
            var guess = Path.Combine(ctx.IntermediateDir, "publish", ctx.Rid);
            if (Directory.Exists(guess)) ctx.PublishDir = guess;
        }
        if (ctx.PublishDir == null)
            _log.Warn("No publish folder available; {publish} globs cannot be checked. Pass --publish-dir or run 'tenon build' first.");

        ctx.ExpandMacros();
        ctx.Harvest();
        if (!ctx.Lint.HasErrors)
        {
            var builder = new MsiBuilder(ctx.Definition, ctx.Files, ctx.Lint, _log);
            var plan = builder.Plan();
            if (!ctx.Lint.HasErrors)
            {
                _log.Info($"{plan.ProductName} {plan.DisplayVersion} ({plan.ArchName}, {plan.Scope.ToString().ToLowerInvariant()})");
                _log.Info($"  install dir  {ctx.Definition.Install.Dir}");
                _log.Info($"  files        {plan.Files.Count()}");
                _log.Info($"  components   {plan.Components.Count}");
                _log.Info($"  features     {string.Join(", ", plan.Features.Select(f => f.Id))}");
                _log.Info($"  shortcuts    {plan.Components.Sum(c => c.Shortcuts.Count)}");
            }
        }

        ctx.ReportLint(includeInfo: true);
        var errors = ctx.Lint.Messages.Count(m => m.Severity == LintSeverity.Error);
        var warnings = ctx.Lint.Messages.Count(m => m.Severity == LintSeverity.Warning);
        _log.Info($"{errors} error(s), {warnings} warning(s)");
        return errors == 0 ? 0 : 2;
    }
}
