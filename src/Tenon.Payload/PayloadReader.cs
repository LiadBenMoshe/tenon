using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tenon.Payload;

/// <summary>Reads payload entries appended to a setup executable (usually the running process itself).</summary>
public sealed class PayloadReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly Dictionary<string, PayloadEntry> _entries;

    private PayloadReader(FileStream stream, PayloadIndex index)
    {
        _stream = stream;
        _entries = index.Entries.ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);
    }

    public string FilePath => _stream.Name;
    public IReadOnlyCollection<PayloadEntry> Entries => _entries.Values;

    /// <summary>Opens the payload of a file. Returns null when the file has no Tenon payload.</summary>
    public static PayloadReader? TryOpen(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            var footerEnd = FindPayloadEnd(stream);
            if (footerEnd < PayloadFormat.FooterLength) { stream.Dispose(); return null; }

            var footer = new byte[PayloadFormat.FooterLength];
            stream.Seek(footerEnd - PayloadFormat.FooterLength, SeekOrigin.Begin);
            ReadExactly(stream, footer, footer.Length);
            for (var i = 0; i < 8; i++) if (footer[i] != PayloadFormat.Magic[i]) { stream.Dispose(); return null; }

            var indexOffset = BitConverter.ToInt64(footer, 8);
            var indexLength = BitConverter.ToInt64(footer, 16);
            var version = BitConverter.ToInt32(footer, 24);
            var crc = BitConverter.ToUInt32(footer, 28);
            if (version != PayloadFormat.Version) throw new InvalidDataException($"Unsupported payload version {version}.");
            if (indexOffset < 0 || indexLength <= 0 || indexOffset + indexLength > footerEnd) throw new InvalidDataException("Payload index out of range.");

            var indexBytes = new byte[indexLength];
            stream.Seek(indexOffset, SeekOrigin.Begin);
            ReadExactly(stream, indexBytes, indexBytes.Length);
            if (Crc32.Compute(indexBytes, 0, indexBytes.Length) != crc) throw new InvalidDataException("Payload index is corrupt (CRC mismatch).");

            var index = JsonSerializer.Deserialize<PayloadIndex>(indexBytes, PayloadFormat.JsonOptions) ?? throw new InvalidDataException("Payload index is empty.");
            return new PayloadReader(stream, index);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static PayloadReader Open(string path)
        => TryOpen(path) ?? throw new InvalidDataException($"{Path.GetFileName(path)} does not contain a setup payload.");

    public bool Contains(string name) => _entries.ContainsKey(name);

    public PayloadEntry GetEntry(string name)
        => _entries.TryGetValue(name, out var e) ? e : throw new FileNotFoundException($"Payload entry '{name}' not found.");

    public byte[] ReadBytes(string name)
    {
        var e = GetEntry(name);
        var data = new byte[e.Length];
        lock (_stream)
        {
            _stream.Seek(e.Offset, SeekOrigin.Begin);
            ReadExactly(_stream, data, data.Length);
        }
        return data;
    }

    public string ReadText(string name) => Encoding.UTF8.GetString(ReadBytes(name));

    public SetupManifest ReadManifest() => SetupManifest.FromJson(ReadText(PayloadFormat.ManifestEntry));

    /// <summary>Extracts an entry to disk and verifies its hash.</summary>
    public string Extract(string name, string destinationPath)
    {
        var e = GetEntry(name);
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var sha = SHA256.Create();
        using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1 << 16];
            var remaining = e.Length;
            lock (_stream)
            {
                _stream.Seek(e.Offset, SeekOrigin.Begin);
                while (remaining > 0)
                {
                    var read = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0) throw new EndOfStreamException();
                    output.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }
            }
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = PayloadWriter.ToHex(sha.Hash!);
        if (!string.Equals(hash, e.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destinationPath);
            throw new InvalidDataException($"Payload entry '{name}' is corrupt (hash mismatch).");
        }
        return destinationPath;
    }

    /// <summary>
    /// Returns the offset where the payload footer ends: the start of the Authenticode certificate table
    /// when the file is signed, otherwise the file length.
    /// </summary>
    private static long FindPayloadEnd(FileStream s)
    {
        var length = s.Length;
        try
        {
            var header = new byte[64];
            s.Seek(0, SeekOrigin.Begin);
            ReadExactly(s, header, 64);
            if (header[0] != 'M' || header[1] != 'Z') return length;
            var peOffset = BitConverter.ToInt32(header, 0x3C);
            var pe = new byte[24 + 240];
            s.Seek(peOffset, SeekOrigin.Begin);
            ReadExactly(s, pe, pe.Length);
            if (pe[0] != 'P' || pe[1] != 'E') return length;
            var magic = BitConverter.ToUInt16(pe, 24);
            var dataDirOffset = magic == 0x20B ? 24 + 112 : 24 + 96; // PE32+ vs PE32
            var securityDir = dataDirOffset + 4 * 8; // IMAGE_DIRECTORY_ENTRY_SECURITY = 4
            var certOffset = BitConverter.ToUInt32(pe, securityDir);
            var certSize = BitConverter.ToUInt32(pe, securityDir + 4);
            if (certOffset != 0 && certSize != 0 && certOffset < length) return certOffset;
        }
        catch
        {
            // not a PE or truncated: treat as unsigned
        }
        return length;
    }

    private static void ReadExactly(Stream s, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = s.Read(buffer, total, count - total);
            if (read <= 0) throw new EndOfStreamException();
            total += read;
        }
    }

    public void Dispose() => _stream.Dispose();
}
