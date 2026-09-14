using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tenon.HookHost;

/// <summary>Context handed to the host on stdin (or as a file) by the custom action or the setup UI.</summary>
public sealed class HookRequest
{
    public string Stage { get; set; } = "";
    public string HooksDirectory { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string InstallDir { get; set; } = "";
    public bool PerMachine { get; set; }
    public bool Upgrade { get; set; }
    public string? PreviousVersion { get; set; }
    public bool Uninstall { get; set; }
    public bool Repair { get; set; }
    public bool Elevated { get; set; }
    public int UiLevel { get; set; } = 2;
    public Dictionary<string, string> Properties { get; set; } = new();
    public List<string> Tasks { get; set; } = new();
    public string DataDirectory { get; set; } = "";

    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static HookRequest FromJson(string json) => JsonSerializer.Deserialize<HookRequest>(json, Options) ?? new HookRequest();
}

/// <summary>One JSON line written by the host to stdout.</summary>
public sealed class HookEvent
{
    public string Type { get; set; } = ""; // log | warning | progress | setProperty | reboot | error | done
    public string? Message { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public int ExitCode { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, HookRequest.Options);
    public static HookEvent? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<HookEvent>(json, HookRequest.Options); } catch { return null; }
    }
}
