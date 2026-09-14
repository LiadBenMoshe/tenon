using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Tenon.Setup.Engine.Elevation;

/// <summary>
/// Runs the setup plan in an elevated copy of this process (one UAC prompt) and relays its progress.
/// The UI process owns the named pipe server; the elevated helper connects as a client.
/// </summary>
public sealed class ElevationClient
{
    private readonly SetupLog _log;

    public ElevationClient(SetupLog log)
    {
        _log = log;
    }

    public async Task<SetupResult> RunAsync(ElevatedPlan plan, string tempDir, IProgress<SetupProgress> progress, CancellationToken ct)
    {
        var pipeName = "tenon-" + Guid.NewGuid().ToString("N");
        var planPath = Path.Combine(tempDir, "plan-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(planPath, plan.ToJson());

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var exe = Environment.ProcessPath!;
        var args = $"--elevated --pipe {pipeName} --plan \"{planPath}\"";
        _log.Info($"Elevating: \"{exe}\" {args}");

        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(exe) })
                      ?? throw new InvalidOperationException("Could not start the elevated helper.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _log.Warn("Administrator permission was declined.");
            return new SetupResult { ExitCode = 1602, ErrorMessage = "Administrator permission is required to install for all users.", Hint = "Choose 'Only me' to install without administrator rights." };
        }

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(120));
            await server.WaitForConnectionAsync(connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) { try { process.Kill(); } catch { /* ignore */ } }
            return new SetupResult { ExitCode = 1603, ErrorMessage = "The elevated setup process did not start." };
        }

        using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var cancelReg = ct.Register(() =>
        {
            try { writer.WriteLine(new PipeMessage { Type = "cancel" }.ToJson()); } catch { /* pipe gone */ }
        });

        SetupResult? result = null;
        while (true)
        {
            string? line;
            try { line = await reader.ReadLineAsync(); }
            catch (IOException) { break; }
            if (line == null) break;
            var msg = PipeMessage.FromJson(line);
            if (msg == null) continue;
            switch (msg.Type)
            {
                case "progress":
                    progress.Report(new SetupProgress(msg.Percent, msg.Status ?? "", msg.Detail));
                    break;
                case "log":
                    if (msg.Line != null) _log.Info("[elevated] " + msg.Line);
                    break;
                case "done":
                    result = new SetupResult { ExitCode = msg.ExitCode, RebootRequired = msg.RebootRequired, ErrorMessage = msg.Error, Hint = msg.Hint };
                    break;
            }
            if (result != null) break;
        }

        process.WaitForExit(10000);
        try { File.Delete(planPath); } catch { /* ignore */ }
        return result ?? new SetupResult { ExitCode = process.HasExited ? process.ExitCode : 1603, ErrorMessage = "The elevated setup process ended unexpectedly. See the log for details." };
    }
}
