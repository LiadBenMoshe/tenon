using System;
using System.Runtime.InteropServices;

namespace Tenon.Native
{
    /// <summary>version.dll helpers used to read the language ids of a versioned file.</summary>
    public static class VersionResourceApi
    {
        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileVersionInfoSizeW(string fileName, out uint handle);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetFileVersionInfoW(string fileName, uint handle, uint len, byte[] data);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool VerQueryValueW(byte[] block, string subBlock, out IntPtr buffer, out uint len);

        /// <summary>Returns the language ids from the VarFileInfo\Translation block, or an empty array.</summary>
        public static int[] GetLanguages(string path)
        {
            try
            {
                var size = GetFileVersionInfoSizeW(path, out _);
                if (size == 0) return new int[0];
                var data = new byte[size];
                if (!GetFileVersionInfoW(path, 0, size, data)) return new int[0];
                if (!VerQueryValueW(data, "\\VarFileInfo\\Translation", out var ptr, out var len) || len < 4) return new int[0];
                var count = (int)(len / 4);
                var result = new int[count];
                for (var i = 0; i < count; i++)
                {
                    result[i] = Marshal.ReadInt16(ptr, i * 4) & 0xFFFF;
                }
                return result;
            }
            catch
            {
                return new int[0];
            }
        }
    }
}
