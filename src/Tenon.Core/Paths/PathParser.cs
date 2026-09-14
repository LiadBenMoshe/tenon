using System.Text.RegularExpressions;

namespace Tenon.Core.Paths;

/// <summary>A target path split into its leading {constant} and the remaining segments.</summary>
public sealed record ParsedPath(string Root, IReadOnlyList<string> Segments)
{
    public bool IsRootOnly => Segments.Count == 0;

    public string FileName => Segments.Count == 0 ? "" : Segments[Segments.Count - 1];

    /// <summary>The same path without its last segment.</summary>
    public ParsedPath Parent => new(Root, Segments.Take(Math.Max(0, Segments.Count - 1)).ToList());

    public ParsedPath Append(params string[] more) => new(Root, Segments.Concat(more.Where(s => s.Length > 0)).ToList());

    public override string ToString() => Segments.Count == 0 ? "{" + Root + "}" : "{" + Root + "}\\" + string.Join("\\", Segments);
}

/// <summary>Parses Inno-style paths such as {app}\plugins\x.dll.</summary>
public static class PathParser
{
    private static readonly Regex ConstantRegex = new(@"^\{([a-zA-Z0-9]+)\}", RegexOptions.Compiled);

    public static readonly IReadOnlyList<string> KnownConstants = new[]
    {
        "app", "autopf", "pf", "pf32", "commonfiles", "commonfiles32",
        "localappdata", "appdata", "commonappdata",
        "desktop", "group", "startmenu", "startup",
        "sys", "win", "temp", "fonts",
        "publish", "src",
    };

    public static bool IsKnownConstant(string name) => KnownConstants.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool StartsWithConstant(string path) => ConstantRegex.IsMatch(path);

    /// <summary>Parses a path. Paths without a leading constant are treated as relative to {app}.</summary>
    public static ParsedPath Parse(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        path = path.Trim();
        var root = "app";
        var rest = path;
        var m = ConstantRegex.Match(path);
        if (m.Success)
        {
            root = m.Groups[1].Value.ToLowerInvariant();
            rest = path.Substring(m.Length);
        }

        var segments = rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && s != ".")
            .ToList();

        return new ParsedPath(root, segments);
    }

    /// <summary>Replaces {constant} prefixes that refer to build-time folders ({publish}, {src}) with real paths.</summary>
    public static string ResolveBuildTime(string path, IReadOnlyDictionary<string, string> buildConstants)
    {
        var m = ConstantRegex.Match(path.Trim());
        if (!m.Success) return path;
        var name = m.Groups[1].Value;
        if (buildConstants.TryGetValue(name, out var real))
        {
            var rest = path.Trim().Substring(m.Length).TrimStart('\\', '/');
            return rest.Length == 0 ? real : Path.Combine(real, rest);
        }
        return path;
    }
}
