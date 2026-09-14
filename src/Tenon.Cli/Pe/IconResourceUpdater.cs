using System.Runtime.InteropServices;
using System.Text;

namespace Tenon.Cli.Pe;

/// <summary>
/// Replaces the application icon of an executable with the images of an .ico file.
/// Works on .NET single-file bundles: Win32 resource updates rewrite the PE image and drop the data
/// appended after it, so the bundle is detached first, re-attached afterwards and its absolute
/// offsets (the marker in the host plus every manifest entry) are shifted accordingly.
/// </summary>
public static class IconResourceUpdater
{
    private const int RT_ICON = 3;
    private const int RT_GROUP_ICON = 14;

    // SHA-256 of ".net core bundle": the marker that precedes the bundle header offset in the host.
    private static readonly byte[] BundleSignature =
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
    };

    public static void Apply(string exePath, string icoPath)
    {
        var images = ReadIco(icoPath);
        if (images.Count == 0) throw new InvalidDataException($"{icoPath} contains no images.");

        var original = File.ReadAllBytes(exePath);
        var overlayStart = PeImageEnd(original);
        var overlay = new byte[original.Length - overlayStart];
        Array.Copy(original, overlayStart, overlay, 0, overlay.Length);

        // 1. Update the icon on the bare PE image.
        var temp = exePath + ".rsrc.tmp";
        File.WriteAllBytes(temp, original.AsSpan(0, overlayStart).ToArray());
        try
        {
            UpdateIcon(temp, images);
            var patched = File.ReadAllBytes(temp);
            var newImageEnd = PeImageEnd(patched);
            if (newImageEnd < patched.Length) patched = patched.AsSpan(0, newImageEnd).ToArray();

            // 2. Re-attach the overlay and fix the bundle offsets.
            var delta = (long)patched.Length - overlayStart;
            if (overlay.Length > 0 && delta != 0) ShiftBundle(patched, overlay, overlayStart, delta);

            using var output = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
            output.Write(patched, 0, patched.Length);
            output.Write(overlay, 0, overlay.Length);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* ignore */ }
        }
    }

    /// <summary>End of the mapped PE image: the highest raw-data end of any section (the overlay starts there).</summary>
    private static int PeImageEnd(byte[] pe)
    {
        var peOffset = BitConverter.ToInt32(pe, 0x3C);
        var sectionCount = BitConverter.ToUInt16(pe, peOffset + 6);
        var optionalSize = BitConverter.ToUInt16(pe, peOffset + 20);
        var sectionTable = peOffset + 24 + optionalSize;
        var end = 0;
        for (var i = 0; i < sectionCount; i++)
        {
            var s = sectionTable + i * 40;
            var rawSize = BitConverter.ToInt32(pe, s + 16);
            var rawPtr = BitConverter.ToInt32(pe, s + 20);
            end = Math.Max(end, rawPtr + rawSize);
        }
        // A certificate table (signed stub) also belongs to the image; Tenon signs after appending, so ignore it.
        return Math.Min(end, pe.Length);
    }

    private static void ShiftBundle(byte[] host, byte[] overlay, long oldOverlayStart, long delta)
    {
        var sig = IndexOf(host, BundleSignature);
        if (sig < 8) return; // not a single-file bundle
        var headerOffset = BitConverter.ToInt64(host, sig - 8);
        if (headerOffset == 0) return;
        BitConverter.GetBytes(headerOffset + delta).CopyTo(host, sig - 8);

        // The manifest lives in the overlay; walk it and shift every absolute offset.
        var m = (int)(headerOffset - oldOverlayStart);
        if (m < 0 || m >= overlay.Length) throw new InvalidDataException("Bundle header is outside the overlay.");
        var major = BitConverter.ToInt32(overlay, m); m += 4;
        var minor = BitConverter.ToInt32(overlay, m); m += 4;
        var fileCount = BitConverter.ToInt32(overlay, m); m += 4;
        m += ReadStringLength(overlay, m); // bundle id
        if (major >= 2)
        {
            ShiftInt64(overlay, ref m, delta); m += 8; // deps.json offset, size
            ShiftInt64(overlay, ref m, delta); m += 8; // runtimeconfig.json offset, size
            m += 8;                                     // flags
        }
        for (var i = 0; i < fileCount; i++)
        {
            ShiftInt64(overlay, ref m, delta); // offset
            m += 8;                            // size
            if (major >= 6) m += 8;            // compressed size
            m += 1;                            // type
            m += ReadStringLength(overlay, m); // relative path
        }
        _ = minor;
    }

    private static void ShiftInt64(byte[] data, ref int pos, long delta)
    {
        var value = BitConverter.ToInt64(data, pos);
        if (value != 0) BitConverter.GetBytes(value + delta).CopyTo(data, pos);
        pos += 8;
    }

    /// <summary>Length in bytes of a BinaryWriter string (7-bit encoded length prefix + UTF-8 payload).</summary>
    private static int ReadStringLength(byte[] data, int pos)
    {
        var length = 0; var shift = 0; var bytes = 0;
        while (true)
        {
            var b = data[pos + bytes++];
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return bytes + length;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack[i] != needle[0]) continue;
            var match = true;
            for (var j = 1; j < needle.Length; j++) if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ Win32 resource update

    private static void UpdateIcon(string exePath, List<IcoImage> images)
    {
        var existing = EnumerateIconResources(exePath);
        var handle = BeginUpdateResourceW(exePath, false);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "BeginUpdateResource failed");
        var committed = false;
        try
        {
            foreach (var (type, name, lang) in existing)
                UpdateResourceW(handle, (IntPtr)type, name, lang, IntPtr.Zero, 0);

            const ushort language = 0x0409;
            var group = new MemoryStream();
            var w = new BinaryWriter(group);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
            for (var i = 0; i < images.Count; i++)
            {
                var img = images[i];
                var id = (ushort)(i + 1);
                Write(handle, RT_ICON, id, language, img.Data);
                w.Write(img.Width); w.Write(img.Height); w.Write(img.Colors); w.Write((byte)0);
                w.Write(img.Planes); w.Write(img.BitCount); w.Write((uint)img.Data.Length); w.Write(id);
            }
            Write(handle, RT_GROUP_ICON, 1, language, group.ToArray());

            if (!EndUpdateResourceW(handle, false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EndUpdateResource failed");
            committed = true;
        }
        finally
        {
            if (!committed) EndUpdateResourceW(handle, true);
        }
    }

    private sealed record IcoImage(byte Width, byte Height, byte Colors, ushort Planes, ushort BitCount, byte[] Data);

    private static List<IcoImage> ReadIco(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (r.ReadUInt16() != 0 || r.ReadUInt16() != 1) throw new InvalidDataException($"{path} is not an .ico file.");
        var count = r.ReadUInt16();
        var images = new List<IcoImage>();
        for (var i = 0; i < count; i++)
        {
            var width = r.ReadByte(); var height = r.ReadByte(); var colors = r.ReadByte(); r.ReadByte();
            var planes = r.ReadUInt16(); var bpp = r.ReadUInt16();
            var size = r.ReadUInt32(); var offset = r.ReadUInt32();
            var data = new byte[size];
            Array.Copy(bytes, offset, data, 0, size);
            images.Add(new IcoImage(width, height, colors, planes, bpp, data));
        }
        return images;
    }

    private static void Write(IntPtr handle, int type, ushort id, ushort lang, byte[] data)
    {
        var ptr = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, ptr, data.Length);
            if (!UpdateResourceW(handle, (IntPtr)type, (IntPtr)id, lang, ptr, (uint)data.Length))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "UpdateResource failed");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static List<(int type, IntPtr name, ushort lang)> EnumerateIconResources(string exePath)
    {
        var result = new List<(int, IntPtr, ushort)>();
        var module = LoadLibraryExW(exePath, IntPtr.Zero, 0x00000002 /* LOAD_LIBRARY_AS_DATAFILE */);
        if (module == IntPtr.Zero) return result;
        try
        {
            foreach (var type in new[] { RT_GROUP_ICON, RT_ICON })
            {
                EnumResourceNamesW(module, (IntPtr)type, (_, t, name, _) =>
                {
                    EnumResourceLanguagesW(module, t, name, (_, _, _, lang, _) => { result.Add(((int)t, name, lang)); return true; }, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            }
        }
        finally
        {
            FreeLibrary(module);
        }
        return result.Where(r => (long)r.Item2 < 0x10000).ToList();
    }

    private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    private delegate bool EnumResLangProc(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr BeginUpdateResourceW(string fileName, [MarshalAs(UnmanagedType.Bool)] bool deleteExisting);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateResourceW(IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool EndUpdateResourceW(IntPtr update, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceLanguagesW(IntPtr module, IntPtr type, IntPtr name, EnumResLangProc callback, IntPtr param);
}
