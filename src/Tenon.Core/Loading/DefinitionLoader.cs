using System.Text.Json;
using System.Text.Json.Serialization;
using Tenon.Core.Model;

namespace Tenon.Core.Loading;

/// <summary>A loaded tenon.json together with its location.</summary>
public sealed class DefinitionDocument
{
    public DefinitionDocument(InstallerDefinition definition, string path)
    {
        Definition = definition;
        Path = System.IO.Path.GetFullPath(path);
        Directory = System.IO.Path.GetDirectoryName(Path) ?? System.IO.Directory.GetCurrentDirectory();
    }

    public InstallerDefinition Definition { get; }
    public string Path { get; }
    public string Directory { get; }

    /// <summary>Resolves a path from the definition relative to the definition folder.</summary>
    public string Resolve(string relativeOrAbsolute)
        => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, relativeOrAbsolute));
}

public static class DefinitionLoader
{
    public static readonly string[] DefaultFileNames = { "tenon.json", "installer.json" };

    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    /// <summary>Finds tenon.json starting at the given file or directory.</summary>
    public static string Locate(string? pathOrDirectory)
    {
        if (!string.IsNullOrEmpty(pathOrDirectory) && File.Exists(pathOrDirectory))
            return Path.GetFullPath(pathOrDirectory!);

        var dir = string.IsNullOrEmpty(pathOrDirectory) ? Directory.GetCurrentDirectory() : Path.GetFullPath(pathOrDirectory!);
        foreach (var name in DefaultFileNames)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"No tenon.json found in '{dir}'. Run 'tenon init' to create one.");
    }

    public static DefinitionDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        InstallerDefinition? def;
        try
        {
            def = JsonSerializer.Deserialize<InstallerDefinition>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: invalid JSON at line {ex.LineNumber + 1}: {ex.Message}", ex);
        }

        if (def == null) throw new InvalidDataException($"{path}: file is empty.");
        return new DefinitionDocument(def, path);
    }

    public static string Serialize(InstallerDefinition definition)
        => JsonSerializer.Serialize(definition, JsonOptions);

    public static void Save(InstallerDefinition definition, string path)
        => File.WriteAllText(path, Serialize(definition) + Environment.NewLine);
}
