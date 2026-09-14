using System.IO;
using System.Text;

namespace Tenon.Setup.Engine;

/// <summary>Plain text log for the whole setup run. Thread safe; flushes each line.</summary>
public sealed class SetupLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    public SetupLog(string path)
    {
        Path = path;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch
        {
            _writer = null;
        }
    }

    public string Path { get; }

    public event Action<string>? LineWritten;

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);
    public void Debug(string message) => Write("DEBUG", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";
        lock (_gate)
        {
            _writer?.WriteLine(line);
        }
        LineWritten?.Invoke(line);
    }

    public static string DefaultPath(string productName, string suffix = "")
    {
        var safe = string.Concat(productName.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Replace(' ', '_');
        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{safe}_Setup_{DateTime.Now:yyyyMMdd_HHmmss}{suffix}.log");
    }

    public void Dispose() => _writer?.Dispose();
}
