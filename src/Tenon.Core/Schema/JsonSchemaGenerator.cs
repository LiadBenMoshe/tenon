using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tenon.Core.Model;

namespace Tenon.Core.Schema;

/// <summary>
/// Generates a JSON Schema (draft 7) for tenon.json from the model classes, so editors show
/// IntelliSense and validation. Descriptions come from [Description] attributes.
/// </summary>
public static class JsonSchemaGenerator
{
    public static string Generate()
    {
        var definitions = new JsonObject();
        var root = TypeSchema(typeof(InstallerDefinition), definitions);
        root["$schema"] = "http://json-schema.org/draft-07/schema#";
        root["$id"] = "https://tenon.dev/schema/v1/tenon.schema.json";
        root["title"] = "Tenon installer definition";
        root["description"] = "Describes how to package a .NET desktop application into an MSI and Setup.exe.";
        if (definitions.Count > 0) root["definitions"] = definitions;

        // Move $schema to the top for readability.
        var ordered = new JsonObject();
        foreach (var key in new[] { "$schema", "$id", "title", "description" }) if (root[key] != null) ordered[key] = root[key]!.DeepClone();
        foreach (var kv in root) if (!ordered.ContainsKey(kv.Key)) ordered[kv.Key] = kv.Value?.DeepClone();
        return ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject TypeSchema(Type type, JsonObject definitions)
    {
        var obj = new JsonObject { ["type"] = "object", ["additionalProperties"] = false };
        var props = new JsonObject();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite) continue;
            var name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            var schema = PropertySchema(p.PropertyType, definitions);
            var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (desc != null) schema["description"] = desc;
            var def = DefaultValue(type, p);
            if (def != null) schema["default"] = def;
            props[name] = schema;
        }
        obj["properties"] = props;
        return obj;
    }

    private static JsonNode? DefaultValue(Type declaring, PropertyInfo p)
    {
        try
        {
            var instance = Activator.CreateInstance(declaring);
            var v = p.GetValue(instance);
            return v switch
            {
                null => null,
                string s => s,
                bool b => b,
                int i => i,
                Enum e => JsonNamingPolicy.CamelCase.ConvertName(e.ToString()),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static JsonObject PropertySchema(Type t, JsonObject definitions)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;
        if (underlying == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (underlying == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (underlying == typeof(int) || underlying == typeof(long)) return new JsonObject { ["type"] = "integer" };
        if (underlying == typeof(double) || underlying == typeof(float)) return new JsonObject { ["type"] = "number" };
        if (underlying == typeof(object)) return new JsonObject();
        if (underlying.IsEnum)
        {
            var values = new JsonArray();
            foreach (var name in Enum.GetNames(underlying)) values.Add(JsonNamingPolicy.CamelCase.ConvertName(name));
            return new JsonObject { ["type"] = "string", ["enum"] = values };
        }
        if (typeof(IEnumerable).IsAssignableFrom(underlying) && underlying.IsGenericType)
        {
            var item = underlying.GetGenericArguments()[0];
            return new JsonObject { ["type"] = "array", ["items"] = PropertySchema(item, definitions) };
        }
        if (underlying.IsClass)
        {
            var key = underlying.Name;
            if (!definitions.ContainsKey(key))
            {
                definitions[key] = new JsonObject(); // placeholder to break cycles
                definitions[key] = TypeSchema(underlying, definitions);
            }
            return new JsonObject { ["$ref"] = "#/definitions/" + key };
        }
        return new JsonObject();
    }
}
