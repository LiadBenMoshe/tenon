using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tenon.Setup.Engine.Elevation;

/// <summary>What the elevated helper process must do. Written to a temp file by the UI process.</summary>
public sealed class ElevatedPlan
{
    public string Mode { get; set; } = "install"; // install | uninstall | repair | prereqs
    public bool PerMachine { get; set; }
    public string InstallDir { get; set; } = "";
    public List<string> SelectedTasks { get; set; } = new();
    public Dictionary<string, string> Properties { get; set; } = new();
    public bool NoRestart { get; set; }
    public string MsiLogPath { get; set; } = "";
    public string? ExtractedMsi { get; set; }
    public bool DeleteAppData { get; set; }

    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static ElevatedPlan FromJson(string json) => JsonSerializer.Deserialize<ElevatedPlan>(json, JsonOptions) ?? new ElevatedPlan();
}

/// <summary>One JSON line on the pipe between the elevated helper and the UI.</summary>
public sealed class PipeMessage
{
    public string Type { get; set; } = ""; // progress | log | done | cancel | prereq
    public int Percent { get; set; }
    public string? Status { get; set; }
    public string? Detail { get; set; }
    public string? Line { get; set; }
    public int ExitCode { get; set; }
    public bool RebootRequired { get; set; }
    public string? Error { get; set; }
    public string? Hint { get; set; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static PipeMessage? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<PipeMessage>(json, Options); } catch { return null; }
    }
}
