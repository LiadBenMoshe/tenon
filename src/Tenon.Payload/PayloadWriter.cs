using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tenon.Payload;

/// <summary>Builds a Setup.exe by copying the stub and appending payload entries plus the index and footer.</summary>
public sealed class PayloadWriter
{
    private readonly List<(string name, Func<Stream> open)> _entries = new();

    public PayloadWriter AddFile(string name, string path)
    {
        _entries.Add((name, () => File.OpenRead(path)));
        return this;
    }

    public PayloadWriter AddBytes(string name, byte[] bytes)
    {
        _entries.Add((name, () => new MemoryStream(bytes, writable: false)));
        return this;
    }

    public PayloadWriter AddText(string name, string text) => AddBytes(name, Encoding.UTF8.GetBytes(text));

    public IReadOnlyList<string> EntryNames => _entries.Select(e => e.name).ToList();

    /// <summary>Optional step run on the copied stub before the payload is appended (for example, patching the icon).</summary>
    public Action<string>? PrepareStub { get; set; }

    public void Write(string stubPath, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(stubPath, outputPath, overwrite: true);
        File.SetAttributes(outputPath, FileAttributes.Normal);
        PrepareStub?.Invoke(outputPath);

        using var output = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        output.Seek(0, SeekOrigin.End);
        var index = new PayloadIndex();
        var buffer = new byte[1 << 16];

        foreach (var (name, open) in _entries)
        {
            var offset = output.Position;
            using var sha = SHA256.Create();
            using var source = open();
            int read;
            long length = 0;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                sha.TransformBlock(buffer, 0, read, null, 0);
                length += read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            index.Entries.Add(new PayloadEntry { Name = name, Offset = offset, Length = length, Sha256 = ToHex(sha.Hash!) });
        }

        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, PayloadFormat.JsonOptions);
        var indexOffset = output.Position;
        output.Write(indexBytes, 0, indexBytes.Length);

        var footer = new byte[PayloadFormat.FooterLength];
        Array.Copy(PayloadFormat.Magic, 0, footer, 0, 8);
        BitConverter.GetBytes(indexOffset).CopyTo(footer, 8);
        BitConverter.GetBytes((long)indexBytes.Length).CopyTo(footer, 16);
        BitConverter.GetBytes(PayloadFormat.Version).CopyTo(footer, 24);
        BitConverter.GetBytes(Crc32.Compute(indexBytes, 0, indexBytes.Length)).CopyTo(footer, 28);
        output.Write(footer, 0, footer.Length);
    }

    internal static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
