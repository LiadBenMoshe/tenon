using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Tenon.Core.Loading;

/// <summary>
/// Expands $(Name) build macros inside every string property of an object graph.
/// Macro names are case-insensitive. Unknown macros are reported, not silently kept.
/// </summary>
public static class MacroExpander
{
    private static readonly Regex MacroRegex = new(@"\$\(([A-Za-z_][A-Za-z0-9_.]*)\)", RegexOptions.Compiled);

    public static bool ContainsMacro(string? value) => value != null && MacroRegex.IsMatch(value);

    public static string Expand(string value, IReadOnlyDictionary<string, string> props, ICollection<string>? unknown = null)
    {
        return MacroRegex.Replace(value, m =>
        {
            var name = m.Groups[1].Value;
            if (props.TryGetValue(name, out var v)) return v;
            // Unset environment variables expand to nothing, like in MSBuild.
            if (name.StartsWith("env.", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            unknown?.Add(name);
            return m.Value;
        });
    }

    /// <summary>Walks the object graph and expands macros in place. Returns the set of unknown macro names.</summary>
    public static ISet<string> ExpandInPlace(object root, IReadOnlyDictionary<string, string> props)
    {
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, props, unknown, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return unknown;
    }

    private static void Walk(object? obj, IReadOnlyDictionary<string, string> props, ISet<string> unknown, HashSet<object> seen)
    {
        if (obj == null) return;
        var type = obj.GetType();
        if (type.IsPrimitive || type.IsEnum || obj is string || obj is decimal) return;
        if (!seen.Add(obj)) return;

        if (obj is IList list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is string s) list[i] = Expand(s, props, unknown);
                else Walk(list[i], props, unknown, seen);
            }
            return;
        }

        if (obj is IDictionary dict)
        {
            foreach (var key in dict.Keys.Cast<object>().ToList())
            {
                if (dict[key] is string s) dict[key] = Expand(s, props, unknown);
                else Walk(dict[key], props, unknown, seen);
            }
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
            var value = prop.GetValue(obj);
            if (value is string s)
            {
                if (prop.CanWrite && ContainsMacro(s)) prop.SetValue(obj, Expand(s, props, unknown));
            }
            else
            {
                Walk(value, props, unknown, seen);
            }
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
