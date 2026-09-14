using System.Text;
using Tenon.Native;

namespace Tenon.CustomAction;

/// <summary>Thin wrapper over an MSIHANDLE for the duration of one custom action call.</summary>
internal sealed class MsiSession
{
    public MsiSession(uint handle)
    {
        Handle = handle;
    }

    public uint Handle { get; }

    public string this[string property]
    {
        get => MsiApi.GetProperty(Handle, property);
        set => MsiApi.MsiSetPropertyW(Handle, property, value);
    }

    public bool IsDeferred => MsiApi.MsiGetMode(Handle, MsiApi.MSIRUNMODE_SCHEDULED) != 0;
    public bool IsRollback => MsiApi.MsiGetMode(Handle, MsiApi.MSIRUNMODE_ROLLBACK) != 0;

    public void Log(string message) => Message(MsiApi.INSTALLMESSAGE_INFO, "Tenon: " + message);

    public void Error(string message) => Message(MsiApi.INSTALLMESSAGE_ERROR | 0x10 /* MB_ICONHAND */, message);

    /// <summary>Sends ActionData so the external UI can show hook progress text (needs an ActionText template with [1]).</summary>
    public void ActionData(string text)
    {
        var rec = MsiApi.MsiCreateRecord(1);
        try
        {
            MsiApi.MsiRecordSetStringW(rec, 1, text);
            MsiApi.MsiProcessMessage(Handle, MsiApi.INSTALLMESSAGE_ACTIONDATA, rec);
        }
        finally
        {
            MsiApi.MsiCloseHandle(rec);
        }
    }

    private void Message(uint type, string text)
    {
        var rec = MsiApi.MsiCreateRecord(1);
        try
        {
            MsiApi.MsiRecordSetStringW(rec, 0, "[1]");
            MsiApi.MsiRecordSetStringW(rec, 1, text);
            MsiApi.MsiProcessMessage(Handle, type, rec);
        }
        finally
        {
            MsiApi.MsiCloseHandle(rec);
        }
    }

    public void RequestReboot() => MsiApi.MsiSetMode(Handle, MsiApi.MSIRUNMODE_REBOOTATEND, true);

    /// <summary>Parses "key=value;key=value" CustomActionData.</summary>
    public Dictionary<string, string> CustomActionData()
    {
        var data = this["CustomActionData"];
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in data.Split(new[] { CustomActionDataSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0) result[pair.Substring(0, eq)] = pair.Substring(eq + 1);
        }
        return result;
    }

    public const char CustomActionDataSeparator = '>';

    /// <summary>Extracts a Binary table stream to a file. Only valid in immediate custom actions.</summary>
    public bool ExtractBinary(string name, string destination)
    {
        var db = MsiApi.MsiGetActiveDatabase(Handle);
        if (db == 0) return false;
        try
        {
            if (MsiApi.MsiDatabaseOpenViewW(db, "SELECT `Data` FROM `Binary` WHERE `Name` = ?", out var view) != 0) return false;
            try
            {
                var param = MsiApi.MsiCreateRecord(1);
                MsiApi.MsiRecordSetStringW(param, 1, name);
                var rc = MsiApi.MsiViewExecute(view, param);
                MsiApi.MsiCloseHandle(param);
                if (rc != 0) return false;
                if (MsiApi.MsiViewFetch(view, out var rec) != 0) return false;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var file = File.Create(destination);
                    var buffer = new byte[1 << 16];
                    while (true)
                    {
                        var size = (uint)buffer.Length;
                        if (MsiApi.MsiRecordReadStream(rec, 1, buffer, ref size) != 0 || size == 0) break;
                        file.Write(buffer, 0, (int)size);
                    }
                    return true;
                }
                finally
                {
                    MsiApi.MsiCloseHandle(rec);
                }
            }
            finally
            {
                MsiApi.MsiViewClose(view);
                MsiApi.MsiCloseHandle(view);
            }
        }
        finally
        {
            MsiApi.MsiCloseHandle(db);
        }
    }
}
