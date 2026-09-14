using System.IO;
using System.Windows;
using System.Windows.Media;
using Tenon.Payload;
using Tenon.Setup.Engine;
using Tenon.Setup.Ui;

namespace Tenon.Setup;

public partial class App : Application
{
    private SetupEngine? _engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = SetupArgs.Parse(e.Args);
        if (args.ShowHelp)
        {
            MessageBox.Show(SetupArgs.HelpText, "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        PayloadReader? payload;
        SetupManifest manifest;
        try
        {
            payload = PayloadReader.TryOpen(Environment.ProcessPath!);
            if (payload == null)
            {
                MessageBox.Show("This executable is the Tenon setup stub without a product payload.\nUse 'tenon build' to create a real installer.",
                    "Tenon Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }
            manifest = payload.ReadManifest();
        }
        catch (Exception ex)
        {
            MessageBox.Show("The setup package is damaged:\n" + ex.Message, "Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1603);
            return;
        }

        var logPath = args.LogPath ?? SetupLog.DefaultPath(manifest.ProductName);
        if (args.Elevated) logPath = Path.ChangeExtension(logPath, null) + "_elevated.log";
        var log = new SetupLog(logPath);
        _engine = new SetupEngine(payload, manifest, args, log)
        {
            MsiLogPath = Path.ChangeExtension(logPath, null) + "_msi.log",
        };

        if (args.Elevated && args.PipeName != null && args.PlanFile != null)
        {
            RunElevated(args.PipeName, args.PlanFile);
            return;
        }

        if (_engine.RelaunchFromTempIfNeeded())
        {
            Shutdown(0);
            return;
        }

        if (args.WaitPid is int waitPid)
        {
            // Started by Tenon.Update from inside the application: let it exit before touching its files.
            try
            {
                using var other = System.Diagnostics.Process.GetProcessById(waitPid);
                log.Info($"Waiting for process {waitPid} to exit");
                other.WaitForExit(60000);
            }
            catch
            {
                // already gone
            }
        }

        if (args.ExtractMsiPath != null)
        {
            File.Copy(_engine.ExtractMsi(), args.ExtractMsiPath, overwrite: true);
            Shutdown(0);
            return;
        }
        if (args.Mode == SetupMode.Layout && args.LayoutDir != null)
        {
            Directory.CreateDirectory(args.LayoutDir);
            File.Copy(_engine.ExtractMsi(), Path.Combine(args.LayoutDir, Path.GetFileName(_engine.ExtractMsi())), overwrite: true);
            File.Copy(Environment.ProcessPath!, Path.Combine(args.LayoutDir, Path.GetFileName(Environment.ProcessPath!)), overwrite: true);
            Shutdown(0);
            return;
        }

        ThemeManager.Apply(this, manifest.Ui);

        if (args.Quiet)
        {
            RunQuiet(args);
            return;
        }

        var vm = new SetupViewModel(_engine, exitCode => Shutdown(exitCode));
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        vm.Start();
    }

    private async void RunElevated(string pipeName, string planFile)
    {
        var exit = await Tenon.Setup.Engine.Elevation.ElevatedWorker.RunAsync(_engine!, pipeName, planFile);
        Shutdown(exit);
    }

    private async void RunQuiet(SetupArgs args)
    {
        var progress = new Progress<SetupProgress>(p => _engine!.Log.Debug($"{p.Percent}% {p.Status} {p.Detail}"));
        SetupResult result;
        try
        {
            if (args.Mode == SetupMode.Uninstall)
            {
                result = _engine!.IsInstalled ? await _engine.UninstallAsync(progress, CancellationToken.None) : new SetupResult { ExitCode = 0 };
            }
            else if (args.Mode == SetupMode.Repair)
            {
                result = _engine!.IsInstalled ? await _engine.RepairAsync(progress, CancellationToken.None) : new SetupResult { ExitCode = 1605 };
            }
            else
            {
                result = await _engine!.InstallAsync(progress, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _engine!.Log.Error(ex.ToString());
            result = new SetupResult { ExitCode = 1603, ErrorMessage = ex.Message };
        }
        _engine!.Log.Info($"Exit code {result.ExitCode}{(result.ErrorMessage != null ? ": " + result.ErrorMessage : "")}");
        if (result.Succeeded) _engine.RestartAppIfRequested();
        Shutdown(result.ExitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        base.OnExit(e);
    }
}
