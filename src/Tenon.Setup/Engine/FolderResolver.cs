using System.IO;
using System.Text.RegularExpressions;

namespace Tenon.Setup.Engine;

/// <summary>Resolves {autopf}-style install folder templates to real paths for the chosen scope.</summary>
public static class FolderResolver
{
    private static readonly Regex Constant = new(@"^\{([a-zA-Z0-9]+)\}", RegexOptions.Compiled);

    public static string Resolve(string template, bool perMachine, string arch)
    {
        var m = Constant.Match(template.Trim());
        if (!m.Success) return Environment.ExpandEnvironmentVariables(template);
        var rest = template.Trim().Substring(m.Length).TrimStart('\\', '/');
        var root = m.Groups[1].Value.ToLowerInvariant() switch
        {
            "autopf" => perMachine ? ProgramFiles(arch) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            "pf" => ProgramFiles(arch),
            "pf32" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "localappdata" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "commonappdata" => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "commonfiles" => Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            _ => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        return rest.Length == 0 ? root : Path.Combine(root, rest);
    }

    private static string ProgramFiles(string arch)
    {
        if (arch.Equals("x86", StringComparison.OrdinalIgnoreCase) && Environment.Is64BitOperatingSystem)
            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetEnvironmentVariable("ProgramW6432");
        return !string.IsNullOrEmpty(pf) ? pf : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    }
}
