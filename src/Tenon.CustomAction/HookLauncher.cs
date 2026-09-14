using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tenon.CustomAction;

/// <summary>Starts tenon-hookhost.exe for one stage and relays its JSON events into the Windows Installer session.</summary>
internal static class HookLauncher
{
    public static uint Run(MsiSession session, string stage, Dictionary<string, string> data)
    {
        var hooksDir = data.GetValueOrDefault("HooksDir", "");
        var host = Path.Combine(hooksDir, "tenon-hookhost.exe");
        if (!File.Exists(host))
        {
            session.Error($"The Tenon hook host was not found at {host}.");
            return 1603;
        }

        var request = new Dictionary<string, object>
        {
            ["stage"] = stage,
            ["hooksDirectory"] = Path.Combine(hooksDir, "hooks"),
            ["assemblyName"] = data.GetValueOrDefault("Assembly", ""),
            ["productName"] = data.GetValueOrDefault("ProductName", ""),
            ["version"] = data.GetValueOrDefault("Version", "0.0.0"),
            ["installDir"] = data.GetValueOrDefault("InstallDir", ""),
            ["perMachine"] = data.GetValueOrDefault("AllUsers", "") == "1",
            ["upgrade"] = data.GetValueOrDefault("Upgrade", "").Length > 0,
            ["previousVersion"] = data.TryGetValue("PreviousVersion", out var prev) ? prev : null,
            ["uninstall"] = data.GetValueOrDefault("Remove", "").Equals("ALL", StringComparison.OrdinalIgnoreCase),
            ["repair"] = data.GetValueOrDefault("Reinstall", "").Length > 0,
            ["elevated"] = data.GetValueOrDefault("Elevated", "") == "1",
            ["uiLevel"] = int.TryParse(data.GetValueOrDefault("UILevel", "2"), out var ui) ? ui : 2,
            ["properties"] = data.Where(kv => kv.Key.StartsWith("P_")).ToDictionary(kv => kv.Key.Substring(2), kv => kv.Value),
            ["tasks"] = data.GetValueOrDefault("Tasks", "").Split(',', StringSplitOptions.RemoveEmptyEntries),
            ["dataDirectory"] = Path.Combine(hooksDir, "data"),
        };

        var requestFile = Path.Combine(hooksDir, $"request-{stage}-{Environment.ProcessId}.json");
        File.WriteAllText(requestFile, JsonSerializer.Serialize(request, RequestJsonContext.Default.DictionaryStringObject));

        var psi = new ProcessStartInfo(host, $"--request \"{requestFile}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = hooksDir,
            StandardOutputEncoding = Encoding.UTF8,
        };
        var dotnetRoot = data.GetValueOrDefault("DotnetRoot", "");
        if (dotnetRoot.Length > 0) psi.Environment["DOTNET_ROOT"] = dotnetRoot;
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        session.Log($"Starting hook host for stage {stage}");
        string? error = null;
        using var process = Process.Start(psi);
        if (process == null)
        {
            session.Error("Could not start the hook host.");
            return 1603;
        }

        string? line;
        while ((line = process.StandardOutput.ReadLine()) != null)
        {
            HookEventDto? evt;
            try { evt = JsonSerializer.Deserialize(line, RequestJsonContext.Default.HookEventDto); }
            catch { evt = null; }
            if (evt == null) { session.Log(line); continue; }
            switch (evt.Type)
            {
                case "log": session.Log(evt.Message ?? ""); break;
                case "warning": session.Log("warning: " + evt.Message); break;
                case "progress": session.ActionData(evt.Message ?? ""); break;
                case "setProperty":
                    if (!session.IsDeferred && evt.Name != null) session[evt.Name] = evt.Value ?? "";
                    break;
                case "reboot": session.RequestReboot(); break;
                case "error": error = evt.Message; break;
            }
        }
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (stderr.Length > 0) session.Log("hook host stderr: " + stderr.Trim());

        var exit = (uint)process.ExitCode;
        session.Log($"Hook host exited with {exit}");
        if (exit == 0) return 0;
        if (exit == 1602) return 1602;

        // Missing .NET runtime shows up as a hostfxr failure (exit codes 0x80008081..0x80008096).
        if (exit >= 0x80008080 && exit <= 0x800080FF)
            session.Error("The setup hooks need the .NET Desktop Runtime, which is not installed on this computer.");
        else
            session.Error(error ?? $"Setup hook stage {stage} failed (exit code {exit}).");
        return 1603;
    }
}

internal sealed class HookEventDto
{
    public string Type { get; set; } = "";
    public string? Message { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public int ExitCode { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(HookEventDto))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
internal partial class RequestJsonContext : JsonSerializerContext
{
}
