using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Tenon.Setup.Engine.Elevation;

/// <summary>Entry point of the elevated helper: executes the plan and streams progress back over the pipe.</summary>
public static class ElevatedWorker
{
    public static async Task<int> RunAsync(SetupEngine engine, string pipeName, string planPath)
    {
        var log = engine.Log;
        ElevatedPlan plan;
        try
        {
            plan = ElevatedPlan.FromJson(File.ReadAllText(planPath));
        }
        catch (Exception ex)
        {
            log.Error("Cannot read plan: " + ex);
            return 1603;
        }

        engine.PerMachine = plan.PerMachine;
        engine.InstallDir = plan.InstallDir;
        engine.SelectedTasks.Clear();
        foreach (var t in plan.SelectedTasks) engine.SelectedTasks.Add(t);
        foreach (var kv in plan.Properties) engine.Args.Properties[kv.Key] = kv.Value;
        engine.MsiLogPath = plan.MsiLogPath;
        engine.DeleteAppData = plan.DeleteAppData;
        if (plan.ExtractedMsi != null && File.Exists(plan.ExtractedMsi)) engine.UseExtractedMsi(plan.ExtractedMsi);

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(30000);
        }
        catch (Exception ex)
        {
            log.Error("Cannot connect to the setup UI: " + ex.Message);
            return 1603;
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        var gate = new object();
        void Send(PipeMessage m)
        {
            lock (gate)
            {
                try { writer.WriteLine(m.ToJson()); } catch { /* UI went away */ }
            }
        }

        log.LineWritten += line => Send(new PipeMessage { Type = "log", Line = line });

        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (PipeMessage.FromJson(line)?.Type == "cancel") cts.Cancel();
                }
            }
            catch
            {
                // pipe closed
            }
        });

        var progress = new Progress<SetupProgress>(p => Send(new PipeMessage { Type = "progress", Percent = p.Percent, Status = p.Status, Detail = p.Detail }));
        SetupResult result;
        try
        {
            result = plan.Mode switch
            {
                "uninstall" => await engine.UninstallAsync(progress, cts.Token),
                "repair" => await engine.RepairAsync(progress, cts.Token),
                "prereqs" => await engine.InstallPrerequisitesAsync(progress, cts.Token),
                _ => await engine.InstallAsync(progress, cts.Token),
            };
        }
        catch (Exception ex)
        {
            log.Error(ex.ToString());
            result = new SetupResult { ExitCode = 1603, ErrorMessage = ex.Message };
        }

        Send(new PipeMessage { Type = "done", ExitCode = result.ExitCode, RebootRequired = result.RebootRequired, Error = result.ErrorMessage, Hint = result.Hint });
        try { pipe.WaitForPipeDrain(); } catch { /* ignore */ }
        return result.ExitCode;
    }
}
