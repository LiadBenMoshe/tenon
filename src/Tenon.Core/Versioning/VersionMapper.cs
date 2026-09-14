using System.Text.RegularExpressions;

namespace Tenon.Core.Versioning;

/// <summary>Result of mapping a product version string onto what Windows Installer accepts.</summary>
public sealed record MappedVersion(string DisplayVersion, string MsiVersion, Version Parsed, string? Warning);

/// <summary>
/// Windows Installer only compares the first three parts (major.minor.build, each 0-65535 with build up to 65535).
/// SemVer prerelease tags and the fourth part are kept for display only.
/// </summary>
public static class VersionMapper
{
    private static readonly Regex Leading = new(@"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.Compiled);

    public static MappedVersion Map(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version is empty.", nameof(version));
        var m = Leading.Match(version.Trim());
        if (!m.Success) throw new FormatException($"'{version}' is not a valid version. Use major.minor.patch, e.g. 1.4.2.");

        var major = int.Parse(m.Groups[1].Value);
        var minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        var build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        var rev = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;

        string? warning = null;
        if (major > 255 || minor > 255)
            warning = $"Version '{version}': Windows Installer limits major and minor to 255. Upgrades may not be detected correctly.";
        else if (build > 65535)
            warning = $"Version '{version}': Windows Installer limits the third part to 65535.";
        else if (m.Groups[4].Success && rev != 0)
            warning = $"Version '{version}': Windows Installer ignores the fourth part ('{rev}') when comparing versions. Two builds that differ only there cannot upgrade each other.";

        var msi = $"{major}.{minor}.{build}";
        return new MappedVersion(version.Trim(), msi, new Version(major, minor, build, rev), warning);
    }
}
