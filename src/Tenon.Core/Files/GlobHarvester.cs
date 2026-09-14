using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Tenon.Core.Model;
using Tenon.Core.Paths;
using Tenon.Core.Validation;

namespace Tenon.Core.Files;

/// <summary>One file that will be installed: where it comes from and where it goes.</summary>
public sealed record HarvestedFile(string SourcePath, ParsedPath TargetDirectory, string FileName, FileRule Rule)
{
    public ParsedPath TargetPath => TargetDirectory.Append(FileName);
}

/// <summary>Expands file rules (globs) from tenon.json into concrete source/target pairs.</summary>
public sealed class GlobHarvester
{
    private readonly string _definitionDir;
    private readonly IReadOnlyDictionary<string, string> _buildConstants;
    private readonly LintCollector _lint;

    public GlobHarvester(string definitionDir, IReadOnlyDictionary<string, string> buildConstants, LintCollector lint)
    {
        _definitionDir = definitionDir;
        _buildConstants = buildConstants;
        _lint = lint;
    }

    public IReadOnlyList<HarvestedFile> Harvest(IEnumerable<FileRule> rules)
    {
        var result = new List<HarvestedFile>();
        var index = 0;
        foreach (var rule in rules)
        {
            index++;
            result.AddRange(HarvestRule(rule, $"files[{index - 1}]"));
        }
        return result;
    }

    public IReadOnlyList<HarvestedFile> HarvestRule(FileRule rule, string location)
    {
        var result = new List<HarvestedFile>();
        if (string.IsNullOrWhiteSpace(rule.From))
        {
            _lint.Error(LintIds.SourceFileMissing, "files entry has an empty 'from'.", "Set 'from' to a file, folder or glob.", location);
            return result;
        }

        var target = PathParser.Parse(rule.To ?? "{app}");
        var from = PathParser.ResolveBuildTime(rule.From, _buildConstants);
        from = from.Replace('/', '\\');

        // Split into a literal base directory and a glob pattern.
        var (baseDir, pattern) = SplitGlob(from);
        baseDir = Path.GetFullPath(Path.Combine(_definitionDir, baseDir));

        if (pattern == null)
        {
            // Literal path: file or directory.
            if (File.Exists(baseDir))
            {
                result.Add(new HarvestedFile(baseDir, target, Path.GetFileName(baseDir), rule));
                return result;
            }
            if (Directory.Exists(baseDir))
            {
                pattern = "**";
            }
            else
            {
                _lint.Error(LintIds.SourceFileMissing, $"Source '{rule.From}' was not found (resolved to '{baseDir}').",
                    "Check the path, or run the publish step first when using {publish}.", location);
                return result;
            }
        }

        if (!Directory.Exists(baseDir))
        {
            _lint.Error(LintIds.GlobMatchedNothing, $"Folder '{baseDir}' for glob '{rule.From}' does not exist.",
                "Check the path, or run the publish step first when using {publish}.", location);
            return result;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern.Replace('\\', '/'));
        foreach (var ex in rule.Exclude)
        {
            var e = ex.Replace('\\', '/');
            if (!e.Contains('/')) e = "**/" + e;
            matcher.AddExclude(e);
        }

        var matches = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDir)));
        if (!matches.HasMatches)
        {
            _lint.Warning(LintIds.GlobMatchedNothing, $"Glob '{rule.From}' matched no files in '{baseDir}'.",
                "Check the pattern and exclusions.", location);
            return result;
        }

        foreach (var m in matches.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var rel = m.Path.Replace('/', '\\');
            var source = Path.Combine(baseDir, rel);
            var relDir = Path.GetDirectoryName(rel) ?? "";
            var dirSegments = relDir.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            result.Add(new HarvestedFile(source, target.Append(dirSegments), Path.GetFileName(rel), rule));
        }

        return result;
    }

    /// <summary>Splits "a\b\*.dll" into ("a\b", "*.dll") and "a\b\**" into ("a\b", "**"). Literal paths return a null pattern.</summary>
    internal static (string baseDir, string? pattern) SplitGlob(string path)
    {
        var parts = path.Split('\\');
        var firstWild = Array.FindIndex(parts, p => p.IndexOfAny(new[] { '*', '?', '[' }) >= 0);
        if (firstWild < 0) return (path, null);
        var baseDir = string.Join("\\", parts.Take(firstWild));
        var pattern = string.Join("\\", parts.Skip(firstWild));
        if (baseDir.Length == 0) baseDir = ".";
        return (baseDir, pattern);
    }
}
