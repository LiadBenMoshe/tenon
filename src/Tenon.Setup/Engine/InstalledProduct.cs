using Tenon.Native;

namespace Tenon.Setup.Engine;

/// <summary>An installed product sharing our upgrade code, as reported by Windows Installer.</summary>
public sealed record InstalledProduct(string ProductCode, Version Version, string VersionString, string? InstallLocation, bool PerMachine)
{
    public static IReadOnlyList<InstalledProduct> FindByUpgradeCode(string upgradeCode)
    {
        var result = new List<InstalledProduct>();
        foreach (var code in MsiApi.EnumRelatedProducts(upgradeCode))
        {
            var state = MsiApi.MsiQueryProductStateW(code);
            if (state != MsiApi.INSTALLSTATE_DEFAULT && state != MsiApi.INSTALLSTATE_LOCAL) continue;
            var versionString = MsiApi.GetProductInfo(code, "VersionString") ?? "0.0.0";
            Version.TryParse(versionString, out var version);
            var location = MsiApi.GetProductInfo(code, "InstallLocation");
            var assignment = MsiApi.GetProductInfo(code, "AssignmentType"); // 0 per-user, 1 per-machine
            result.Add(new InstalledProduct(code, version ?? new Version(0, 0, 0), versionString, string.IsNullOrEmpty(location) ? null : location, assignment == "1"));
        }
        return result;
    }
}
