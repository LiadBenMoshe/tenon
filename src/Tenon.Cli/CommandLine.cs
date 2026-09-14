namespace Tenon.Cli;

/// <summary>Tiny argument parser: "command [positional...] [--name value] [--flag] [-x]".</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }
    public List<string> Positional { get; } = new();

    public static CommandLine Parse(string[] args)
    {
        var cl = new CommandLine();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--") || (a.StartsWith("-") && a.Length == 2))
            {
                var name = a.TrimStart('-');
                string? value = null;
                var eq = name.IndexOf('=');
                if (eq > 0)
                {
                    value = name.Substring(eq + 1);
                    name = name.Substring(0, eq);
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !IsBoolFlag(name))
                {
                    value = args[++i];
                }
                cl._options[name] = value;
            }
            else if (cl.Command == null)
            {
                cl.Command = a.ToLowerInvariant();
            }
            else
            {
                cl.Positional.Add(a);
            }
        }
        return cl;
    }

    private static bool IsBoolFlag(string name) => name is "verbose" or "v" or "help" or "h" or "force" or "no-publish" or "msi-only" or "summary" or "no-sign" or "mandatory" or "prerelease" or "ice" or "quiet" or "passive" or "uninstall";

    public string? Option(string name, string? shortName = null)
    {
        if (_options.TryGetValue(name, out var v)) return v;
        if (shortName != null && _options.TryGetValue(shortName, out v)) return v;
        return null;
    }

    public bool Flag(string name, string? shortName = null)
        => _options.ContainsKey(name) || (shortName != null && _options.ContainsKey(shortName));

    public bool Has(string name) => _options.ContainsKey(name);
}
