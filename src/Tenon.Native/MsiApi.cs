using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Tenon.Native
{
    /// <summary>Raw msi.dll declarations. Keep this file free of logic beyond tiny buffer helpers.</summary>
    public static class MsiApi
    {
        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_MORE_DATA = 234;
        public const uint ERROR_NO_MORE_ITEMS = 259;
        public const uint ERROR_INSTALL_USEREXIT = 1602;
        public const uint ERROR_INSTALL_FAILURE = 1603;
        public const uint ERROR_UNKNOWN_PRODUCT = 1605;
        public const uint ERROR_INSTALL_ALREADY_RUNNING = 1618;
        public const uint ERROR_PRODUCT_VERSION = 1638;
        public const uint ERROR_SUCCESS_REBOOT_INITIATED = 1641;
        public const uint ERROR_SUCCESS_REBOOT_REQUIRED = 3010;

        public const int INSTALLUILEVEL_NOCHANGE = 0;
        public const int INSTALLUILEVEL_DEFAULT = 1;
        public const int INSTALLUILEVEL_NONE = 2;
        public const int INSTALLUILEVEL_BASIC = 3;
        public const int INSTALLUILEVEL_REDUCED = 4;
        public const int INSTALLUILEVEL_FULL = 5;

        public const int INSTALLSTATE_UNKNOWN = -1;
        public const int INSTALLSTATE_ABSENT = 2;
        public const int INSTALLSTATE_LOCAL = 3;
        public const int INSTALLSTATE_DEFAULT = 5;
        public const int INSTALLLEVEL_DEFAULT = 0;

        public const uint INSTALLLOGMODE_FATALEXIT = 1u << 0;
        public const uint INSTALLLOGMODE_ERROR = 1u << 1;
        public const uint INSTALLLOGMODE_WARNING = 1u << 2;
        public const uint INSTALLLOGMODE_USER = 1u << 3;
        public const uint INSTALLLOGMODE_INFO = 1u << 4;
        public const uint INSTALLLOGMODE_FILESINUSE = 1u << 5;
        public const uint INSTALLLOGMODE_RESOLVESOURCE = 1u << 6;
        public const uint INSTALLLOGMODE_OUTOFDISKSPACE = 1u << 7;
        public const uint INSTALLLOGMODE_ACTIONSTART = 1u << 8;
        public const uint INSTALLLOGMODE_ACTIONDATA = 1u << 9;
        public const uint INSTALLLOGMODE_PROGRESS = 1u << 10;
        public const uint INSTALLLOGMODE_COMMONDATA = 1u << 11;
        public const uint INSTALLLOGMODE_INITIALIZE = 1u << 12;
        public const uint INSTALLLOGMODE_TERMINATE = 1u << 13;
        public const uint INSTALLLOGMODE_SHOWDIALOG = 1u << 14;
        public const uint INSTALLLOGMODE_RMFILESINUSE = 1u << 25;
        public const uint INSTALLLOGMODE_INSTALLSTART = 1u << 26;
        public const uint INSTALLLOGMODE_INSTALLEND = 1u << 27;
        public const uint INSTALLLOGMODE_VERBOSE = 1u << 12;
        public const uint INSTALLLOGMODE_EXTRADEBUG = 1u << 13;
        public const uint INSTALLLOGMODE_PROPERTYDUMP = 1u << 11;

        public const uint INSTALLMESSAGE_FATALEXIT = 0x00000000;
        public const uint INSTALLMESSAGE_ERROR = 0x01000000;
        public const uint INSTALLMESSAGE_WARNING = 0x02000000;
        public const uint INSTALLMESSAGE_USER = 0x03000000;
        public const uint INSTALLMESSAGE_INFO = 0x04000000;
        public const uint INSTALLMESSAGE_FILESINUSE = 0x05000000;
        public const uint INSTALLMESSAGE_RESOLVESOURCE = 0x06000000;
        public const uint INSTALLMESSAGE_OUTOFDISKSPACE = 0x07000000;
        public const uint INSTALLMESSAGE_ACTIONSTART = 0x08000000;
        public const uint INSTALLMESSAGE_ACTIONDATA = 0x09000000;
        public const uint INSTALLMESSAGE_PROGRESS = 0x0A000000;
        public const uint INSTALLMESSAGE_COMMONDATA = 0x0B000000;
        public const uint INSTALLMESSAGE_INITIALIZE = 0x0C000000;
        public const uint INSTALLMESSAGE_TERMINATE = 0x0D000000;
        public const uint INSTALLMESSAGE_SHOWDIALOG = 0x0E000000;
        public const uint INSTALLMESSAGE_RMFILESINUSE = 0x19000000;
        public const uint INSTALLMESSAGE_INSTALLSTART = 0x1A000000;
        public const uint INSTALLMESSAGE_INSTALLEND = 0x1B000000;
        public const uint INSTALLMESSAGE_TYPEMASK = 0xFF000000;

        public const int IDOK = 1;
        public const int IDCANCEL = 2;
        public const int IDABORT = 3;
        public const int IDRETRY = 4;
        public const int IDIGNORE = 5;
        public const int IDYES = 6;
        public const int IDNO = 7;

        public const uint INSTALLLOGATTRIBUTES_APPEND = 1;
        public const uint INSTALLLOGATTRIBUTES_FLUSHEACHLINE = 2;

        public const uint MSIRUNMODE_SCHEDULED = 7;
        public const uint MSIRUNMODE_ROLLBACK = 8;
        public const uint MSIRUNMODE_COMMIT = 9;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int InstallUIHandlerRecord(IntPtr context, uint messageType, uint hRecord);

        [DllImport("msi.dll")] public static extern int MsiSetInternalUI(int dwUILevel, IntPtr phWnd);
        [DllImport("msi.dll")] public static extern uint MsiSetExternalUIRecord(InstallUIHandlerRecord handler, uint filter, IntPtr context, out InstallUIHandlerRecord previous);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiInstallProductW(string packagePath, string commandLine);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiConfigureProductExW(string productCode, int installLevel, int installState, string commandLine);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, StringBuilder productCode);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiGetProductInfoW(string productCode, string property, StringBuilder buffer, ref uint size);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern int MsiQueryProductStateW(string productCode);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiEnableLogW(uint logMode, string logFile, uint attributes);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiGetPropertyW(uint hInstall, string name, StringBuilder buffer, ref uint size);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiSetPropertyW(uint hInstall, string name, string value);
        [DllImport("msi.dll")] public static extern int MsiProcessMessage(uint hInstall, uint messageType, uint hRecord);
        [DllImport("msi.dll")] public static extern uint MsiGetMode(uint hInstall, uint runMode);
        [DllImport("msi.dll")] public static extern uint MsiSetMode(uint hInstall, uint runMode, [MarshalAs(UnmanagedType.Bool)] bool state);
        public const uint MSIRUNMODE_REBOOTATEND = 11;
        [DllImport("msi.dll")] public static extern uint MsiGetActiveDatabase(uint hInstall);

        [DllImport("msi.dll")] public static extern uint MsiCreateRecord(uint fields);
        [DllImport("msi.dll")] public static extern uint MsiCloseHandle(uint handle);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiRecordSetStringW(uint hRecord, uint field, string value);
        [DllImport("msi.dll")] public static extern uint MsiRecordSetInteger(uint hRecord, uint field, int value);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiRecordGetStringW(uint hRecord, uint field, StringBuilder buffer, ref uint size);
        [DllImport("msi.dll")] public static extern int MsiRecordGetInteger(uint hRecord, uint field);
        [DllImport("msi.dll")] public static extern uint MsiRecordGetFieldCount(uint hRecord);
        [DllImport("msi.dll")] public static extern uint MsiRecordReadStream(uint hRecord, uint field, byte[] buffer, ref uint size);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiFormatRecordW(uint hInstall, uint hRecord, StringBuilder buffer, ref uint size);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out uint hDatabase);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiDatabaseOpenViewW(uint hDatabase, string sql, out uint hView);
        [DllImport("msi.dll")] public static extern uint MsiViewExecute(uint hView, uint hRecord);
        [DllImport("msi.dll")] public static extern uint MsiViewFetch(uint hView, out uint hRecord);
        [DllImport("msi.dll")] public static extern uint MsiViewClose(uint hView);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSIFILEHASHINFO
        {
            public uint dwFileHashInfoSize;
            public int dwData0;
            public int dwData1;
            public int dwData2;
            public int dwData3;
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiGetFileHashW(string filePath, uint options, ref MSIFILEHASHINFO hash);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiGetFileVersionW(string filePath, StringBuilder versionBuf, ref uint versionSize, StringBuilder langBuf, ref uint langSize);

        public static string GetProperty(uint hInstall, string name)
        {
            uint size = 0;
            var rc = MsiGetPropertyW(hInstall, name, null, ref size);
            if (rc != ERROR_SUCCESS && rc != ERROR_MORE_DATA) return string.Empty;
            var sb = new StringBuilder((int)size + 1);
            size = (uint)sb.Capacity;
            rc = MsiGetPropertyW(hInstall, name, sb, ref size);
            return rc == ERROR_SUCCESS ? sb.ToString() : string.Empty;
        }

        public static string RecordGetString(uint hRecord, uint field)
        {
            uint size = 0;
            var rc = MsiRecordGetStringW(hRecord, field, null, ref size);
            if (rc != ERROR_SUCCESS && rc != ERROR_MORE_DATA) return string.Empty;
            var sb = new StringBuilder((int)size + 1);
            size = (uint)sb.Capacity;
            rc = MsiRecordGetStringW(hRecord, field, sb, ref size);
            return rc == ERROR_SUCCESS ? sb.ToString() : string.Empty;
        }

        /// <summary>Formats a record using its field 0 template (works outside a session for UI messages).</summary>
        public static string FormatRecord(uint hRecord)
        {
            uint size = 0;
            var rc = MsiFormatRecordW(0, hRecord, null, ref size);
            if (rc != ERROR_SUCCESS && rc != ERROR_MORE_DATA) return string.Empty;
            var sb = new StringBuilder((int)size + 1);
            size = (uint)sb.Capacity;
            rc = MsiFormatRecordW(0, hRecord, sb, ref size);
            return rc == ERROR_SUCCESS ? sb.ToString() : string.Empty;
        }

        /// <summary>Enumerates product codes sharing an upgrade code.</summary>
        public static System.Collections.Generic.List<string> EnumRelatedProducts(string upgradeCode)
        {
            var result = new System.Collections.Generic.List<string>();
            var sb = new StringBuilder(39);
            for (uint i = 0; ; i++)
            {
                sb.Clear();
                var rc = MsiEnumRelatedProductsW(upgradeCode, 0, i, sb);
                if (rc != ERROR_SUCCESS) break;
                result.Add(sb.ToString());
            }
            return result;
        }

        public static string GetProductInfo(string productCode, string property)
        {
            uint size = 0;
            var rc = MsiGetProductInfoW(productCode, property, null, ref size);
            if (rc != ERROR_SUCCESS && rc != ERROR_MORE_DATA) return null;
            var sb = new StringBuilder((int)size + 1);
            size = (uint)sb.Capacity;
            rc = MsiGetProductInfoW(productCode, property, sb, ref size);
            return rc == ERROR_SUCCESS ? sb.ToString() : null;
        }
    }
}
