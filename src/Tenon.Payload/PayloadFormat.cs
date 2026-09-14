using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tenon.Payload;

/// <summary>
/// Container appended to the setup stub:
///   [stub PE][entry bytes ...][index JSON][footer]
/// footer (32 bytes): "TENONPAY" | index offset (int64 LE) | index length (int64 LE) | version (int32 LE) | CRC32 of index (uint32 LE)
/// When the file is Authenticode-signed after appending, the certificate table follows the footer; the
/// reader locates the footer through the PE security directory instead of the end of file.
/// </summary>
public static class PayloadFormat
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("TENONPAY");
    public const int FooterLength = 32;
    public const int Version = 1;
    public const string ManifestEntry = "manifest.json";

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class PayloadIndex
{
    public List<PayloadEntry> Entries { get; set; } = new();
}

public sealed class PayloadEntry
{
    public string Name { get; set; } = "";
    public long Offset { get; set; }
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>Small CRC-32 (IEEE) used to validate the index before parsing it.</summary>
public static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(byte[] data, int offset, int count)
    {
        var crc = 0xFFFFFFFFu;
        for (var i = offset; i < offset + count; i++) crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
