using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tenon.Update
{
    /// <summary>The releases.&lt;channel&gt;.json document published by 'tenon release'.</summary>
    public sealed class ReleaseFeed
    {
        public int Schema { get; set; } = 1;
        public string Product { get; set; } = "";
        public string UpgradeCode { get; set; } = "";
        public string Channel { get; set; } = "stable";
        public List<ReleaseEntry> Releases { get; set; } = new List<ReleaseEntry>();

        public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var o = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            return o;
        }

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static ReleaseFeed FromJson(string json)
            => JsonSerializer.Deserialize<ReleaseFeed>(json, JsonOptions) ?? throw new InvalidOperationException("The update feed is empty.");
    }

    public sealed class ReleaseEntry
    {
        public string Version { get; set; } = "";
        public DateTimeOffset? Published { get; set; }
        /// <summary>Release notes as Markdown text or a URL.</summary>
        public string Notes { get; set; }
        public bool Mandatory { get; set; }
        public bool Prerelease { get; set; }
        public int? MinOsBuild { get; set; }
        public string Arch { get; set; } = "x64";
        public ReleasePackage Full { get; set; } = new ReleasePackage();
        public List<ReleaseDelta> Deltas { get; set; } = new List<ReleaseDelta>();
    }

    public sealed class ReleasePackage
    {
        /// <summary>Absolute URL, or relative to the feed location.</summary>
        public string Url { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
    }

    public sealed class ReleaseDelta
    {
        public string From { get; set; } = "";
        public string Url { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
    }
}
