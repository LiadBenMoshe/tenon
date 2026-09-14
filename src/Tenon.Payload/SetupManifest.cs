using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tenon.Payload;

/// <summary>
/// The runtime view of tenon.json that Setup.exe needs. Stored as the "manifest.json" payload entry.
/// No build-time paths: every file the stub needs is another payload entry referenced by name.
/// </summary>
public sealed class SetupManifest
{
    public int FormatVersion { get; set; } = 1;
    public string TenonVersion { get; set; } = "";

    public string ProductName { get; set; } = "";
    public string Version { get; set; } = "";
    public string MsiVersion { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string UpgradeCode { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? SupportUrl { get; set; }
    public string Arch { get; set; } = "x64";
    public List<string> Languages { get; set; } = new() { "en-US" };

    /// <summary>perMachine, perUser or either.</summary>
    public string Scope { get; set; } = "either";
    public string DefaultScope { get; set; } = "perUser";
    /// <summary>Install folder with constants, e.g. {autopf}\Company\Product.</summary>
    public string InstallDir { get; set; } = "";
    public bool AllowChangeDir { get; set; } = true;
    public string? MainExe { get; set; }
    public bool RunAfterInstall { get; set; } = true;
    public string CloseRunningApp { get; set; } = "prompt";
    public int? MinWindowsBuild { get; set; }
    public string? MinWindowsDisplay { get; set; }
    public bool ArpNoModify { get; set; }
    public bool ArpNoRepair { get; set; }
    public List<string> DeleteAppDataOnUninstall { get; set; } = new();

    public List<ManifestTask> Tasks { get; set; } = new();
    public ManifestUi Ui { get; set; } = new();
    public List<ManifestPrerequisite> Prerequisites { get; set; } = new();

    /// <summary>Payload entry name of the MSI.</summary>
    public string MsiEntry { get; set; } = "package.msi";
    /// <summary>Payload entry name of the product icon (.ico), if any.</summary>
    public string? IconEntry { get; set; }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SetupManifest FromJson(string json)
        => JsonSerializer.Deserialize<SetupManifest>(json, JsonOptions) ?? throw new InvalidDataException("manifest.json is empty.");
}

public sealed class ManifestTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool Default { get; set; } = true;
}

public sealed class ManifestUi
{
    /// <summary>modern or oneClick.</summary>
    public string Style { get; set; } = "modern";
    public List<string> Pages { get; set; } = new() { "welcome", "license", "folder", "options", "progress", "finish" };
    /// <summary>Payload entry name of the license (.rtf or .txt), if any.</summary>
    public string? LicenseEntry { get; set; }
    public string Accent { get; set; } = "#2563EB";
    public string? Background { get; set; }
    /// <summary>auto, light or dark.</summary>
    public string DarkMode { get; set; } = "auto";
    public string? LogoEntry { get; set; }
    public string? BannerEntry { get; set; }
    public string? Font { get; set; }
}

public sealed class ManifestPrerequisite
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
    public string? RollForward { get; set; }
    /// <summary>Payload entry holding the installer, or null when it must be downloaded.</summary>
    public string? Entry { get; set; }
    public string? Url { get; set; }
    public string? Sha256 { get; set; }
    public string? Args { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>any, perMachine, perUser.</summary>
    public string Requires { get; set; } = "any";
    public ManifestPrerequisiteDetect? Detect { get; set; }
}

public sealed class ManifestPrerequisiteDetect
{
    public string? ProductCode { get; set; }
    public string? UpgradeCode { get; set; }
    public string? RegistryValue { get; set; }
    public string? MinVersion { get; set; }
    public string? File { get; set; }
}
