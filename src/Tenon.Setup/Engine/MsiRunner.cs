using System.Runtime.InteropServices;
using Tenon.Native;

namespace Tenon.Setup.Engine;

public sealed record MsiProgress(int Percent, string? Action, string? Detail);

/// <summary>
/// Runs Windows Installer operations with the internal UI disabled and an external UI record handler,
/// translating progress messages into percentages the WPF UI can show.
/// </summary>
public sealed class MsiRunner
{
    private readonly SetupLog _log;
    private MsiApi.InstallUIHandlerRecord? _handler; // keep alive for the native callback
    private volatile bool _cancel;

    // progress state (see "Handling Progress Messages" in the Windows Installer SDK)
    private int _total, _completed, _step, _resets;
    private bool _forward = true, _enableActionData, _inScript;
    private string? _currentAction;
    private readonly Dictionary<string, string> _actionText = new(StringComparer.OrdinalIgnoreCase)
    {
        ["InstallValidate"] = "Checking the installation",
        ["GenerateScript"] = "Preparing the installation",
        ["INSTALL"] = "Starting",
        ["CostInitialize"] = "Computing space requirements",
        ["FileCost"] = "Computing space requirements",
        ["CostFinalize"] = "Computing space requirements",
        ["LaunchConditions"] = "Checking requirements",
        ["AppSearch"] = "Checking requirements",
        ["FindRelatedProducts"] = "Looking for previous versions",
        ["RollbackCleanup"] = "Cleaning up",
        ["InstallInitialize"] = "Preparing",
        ["ProcessComponents"] = "Updating component registration",
        ["RemoveExistingProducts"] = "Removing the previous version",
        ["RemoveFiles"] = "Removing files",
        ["RemoveShortcuts"] = "Removing shortcuts",
        ["RemoveRegistryValues"] = "Removing registry values",
        ["InstallFiles"] = "Copying files",
        ["CreateShortcuts"] = "Creating shortcuts",
        ["WriteRegistryValues"] = "Writing registry values",
        ["WriteEnvironmentStrings"] = "Updating environment variables",
        ["RegisterProduct"] = "Registering the product",
        ["PublishFeatures"] = "Publishing features",
        ["PublishProduct"] = "Publishing the product",
        ["InstallServices"] = "Installing services",
        ["StartServices"] = "Starting services",
        ["StopServices"] = "Stopping services",
        ["DeleteServices"] = "Removing services",
        ["InstallFinalize"] = "Finishing",
        ["RegisterExtensionInfo"] = "Registering file types",
        ["UnregisterExtensionInfo"] = "Unregistering file types",
        ["UnpublishFeatures"] = "Unpublishing features",
    };

    public MsiRunner(SetupLog log)
    {
        _log = log;
    }

    public event Action<MsiProgress>? Progress;
    public event Action<string>? ErrorMessage;

    /// <summary>Ask the running installation to roll back at the next opportunity.</summary>
    public void Cancel() => _cancel = true;

    public uint Install(string msiPath, string commandLine, string msiLogPath)
    {
        return Run(msiLogPath, () => MsiApi.MsiInstallProductW(msiPath, commandLine));
    }

    public uint Uninstall(string productCode, string commandLine, string msiLogPath)
    {
        return Run(msiLogPath, () => MsiApi.MsiConfigureProductExW(productCode, MsiApi.INSTALLLEVEL_DEFAULT, MsiApi.INSTALLSTATE_ABSENT, commandLine));
    }

    public uint Repair(string productCode, string commandLine, string msiLogPath)
    {
        return Run(msiLogPath, () => MsiApi.MsiConfigureProductExW(productCode, MsiApi.INSTALLLEVEL_DEFAULT, MsiApi.INSTALLSTATE_DEFAULT, "REINSTALL=ALL REINSTALLMODE=omus " + commandLine));
    }

    private uint Run(string msiLogPath, Func<uint> operation)
    {
        _cancel = false;
        _total = _completed = _step = _resets = _lastPercent = 0;
        _holdPercent = false;
        _forward = true;
        _enableActionData = _inScript = false;
        _currentAction = null;

        MsiApi.MsiEnableLogW(MsiApi.INSTALLLOGMODE_VERBOSE | MsiApi.INSTALLLOGMODE_ERROR | MsiApi.INSTALLLOGMODE_WARNING | MsiApi.INSTALLLOGMODE_INFO |
                             MsiApi.INSTALLLOGMODE_ACTIONSTART | MsiApi.INSTALLLOGMODE_ACTIONDATA | MsiApi.INSTALLLOGMODE_PROPERTYDUMP |
                             MsiApi.INSTALLLOGMODE_FATALEXIT | MsiApi.INSTALLLOGMODE_USER | MsiApi.INSTALLLOGMODE_OUTOFDISKSPACE,
            msiLogPath, MsiApi.INSTALLLOGATTRIBUTES_APPEND | MsiApi.INSTALLLOGATTRIBUTES_FLUSHEACHLINE);
        MsiApi.MsiSetInternalUI(MsiApi.INSTALLUILEVEL_NONE, IntPtr.Zero);

        _handler = Handler;
        var filter = MsiApi.INSTALLLOGMODE_PROGRESS | MsiApi.INSTALLLOGMODE_ACTIONSTART | MsiApi.INSTALLLOGMODE_ACTIONDATA |
                     MsiApi.INSTALLLOGMODE_ERROR | MsiApi.INSTALLLOGMODE_FATALEXIT | MsiApi.INSTALLLOGMODE_WARNING | MsiApi.INSTALLLOGMODE_USER |
                     MsiApi.INSTALLLOGMODE_FILESINUSE | MsiApi.INSTALLLOGMODE_RMFILESINUSE | MsiApi.INSTALLLOGMODE_COMMONDATA |
                     MsiApi.INSTALLLOGMODE_INITIALIZE | MsiApi.INSTALLLOGMODE_TERMINATE | MsiApi.INSTALLLOGMODE_OUTOFDISKSPACE;
        MsiApi.MsiSetExternalUIRecord(_handler, filter, IntPtr.Zero, out var previous);
        try
        {
            var rc = operation();
            _log.Info($"Windows Installer returned {rc}");
            return rc;
        }
        finally
        {
            MsiApi.MsiSetExternalUIRecord(null!, 0, IntPtr.Zero, out _);
            MsiApi.MsiEnableLogW(0, null!, 0);
            _handler = null;
        }
    }

    private int Handler(IntPtr context, uint messageType, uint hRecord)
    {
        try
        {
            var type = messageType & MsiApi.INSTALLMESSAGE_TYPEMASK;
            switch (type)
            {
                case MsiApi.INSTALLMESSAGE_PROGRESS:
                    return HandleProgress(hRecord);

                case MsiApi.INSTALLMESSAGE_ACTIONSTART:
                    _currentAction = MsiApi.RecordGetString(hRecord, 1);
                    if (!_actionText.TryGetValue(_currentAction, out var description))
                    {
                        // Fall back to the package's ActionText, minus template placeholders such as "[1]".
                        description = System.Text.RegularExpressions.Regex.Replace(MsiApi.RecordGetString(hRecord, 2), @"\s*\[\d+\]", "").TrimEnd(':', ' ');
                        if (description.Length == 0) description = null;
                    }
                    _enableActionData = false;
                    Report(description, null);
                    return _cancel ? MsiApi.IDCANCEL : MsiApi.IDOK;

                case MsiApi.INSTALLMESSAGE_ACTIONDATA:
                    if (_enableActionData)
                    {
                        _completed += _forward ? _step : -_step;
                    }
                    string? detail = null;
                    if (string.Equals(_currentAction, "InstallFiles", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_currentAction, "RemoveFiles", StringComparison.OrdinalIgnoreCase))
                    {
                        var file = MsiApi.RecordGetString(hRecord, 1);
                        if (!string.IsNullOrEmpty(file)) detail = System.IO.Path.GetFileName(file);
                    }
                    Report(null, detail);
                    return _cancel ? MsiApi.IDCANCEL : MsiApi.IDOK;

                case MsiApi.INSTALLMESSAGE_ERROR:
                case MsiApi.INSTALLMESSAGE_FATALEXIT:
                case MsiApi.INSTALLMESSAGE_WARNING:
                case MsiApi.INSTALLMESSAGE_USER:
                case MsiApi.INSTALLMESSAGE_OUTOFDISKSPACE:
                    var text = MsiApi.FormatRecord(hRecord);
                    if (type == MsiApi.INSTALLMESSAGE_WARNING) _log.Warn(text);
                    else
                    {
                        _log.Error(text);
                        ErrorMessage?.Invoke(text);
                    }
                    // Answer whatever button set was offered with the safest default.
                    var buttons = messageType & 0x0F;
                    return buttons switch
                    {
                        1 => MsiApi.IDCANCEL,  // MB_OKCANCEL
                        2 => MsiApi.IDABORT,   // MB_ABORTRETRYIGNORE
                        3 => MsiApi.IDNO,      // MB_YESNOCANCEL
                        4 => MsiApi.IDNO,      // MB_YESNO
                        5 => MsiApi.IDCANCEL,  // MB_RETRYCANCEL
                        _ => MsiApi.IDOK,
                    };

                case MsiApi.INSTALLMESSAGE_RMFILESINUSE:
                    // Let Restart Manager close and restart the applications that hold our files.
                    _log.Info("Files in use: asking Restart Manager to close the applications.");
                    return MsiApi.IDOK;

                case MsiApi.INSTALLMESSAGE_FILESINUSE:
                    return MsiApi.IDIGNORE;

                case MsiApi.INSTALLMESSAGE_COMMONDATA:
                    return MsiApi.IDOK;

                case MsiApi.INSTALLMESSAGE_INITIALIZE:
                case MsiApi.INSTALLMESSAGE_TERMINATE:
                    return 0;

                default:
                    return 0;
            }
        }
        catch (Exception ex)
        {
            _log.Error("UI handler failed: " + ex);
            return 0;
        }
    }

    private int HandleProgress(uint hRecord)
    {
        var subtype = MsiApi.MsiRecordGetInteger(hRecord, 1);
        switch (subtype)
        {
            case 0: // reset
                _total = MsiApi.MsiRecordGetInteger(hRecord, 2);
                _forward = MsiApi.MsiRecordGetInteger(hRecord, 3) == 0;
                var field4 = MsiApi.MsiRecordGetInteger(hRecord, 4);
                _resets++;
                // Field 4 is 1 while Windows Installer generates the execution script (costing, validation)
                // and 0 while it executes the script. A reset with no ticks (RollbackCleanup) keeps the last value.
                if (_total > 0) _inScript = field4 == 1; else _holdPercent = true;
                _completed = _forward ? 0 : _total;
                _enableActionData = false;
                _log.Debug($"progress reset #{_resets}: total={_total} forward={_forward} field4={field4} action={_currentAction}");
                break;
            case 1: // action info
                _step = MsiApi.MsiRecordGetInteger(hRecord, 2);
                _enableActionData = MsiApi.MsiRecordGetInteger(hRecord, 3) == 1;
                break;
            case 2: // progress report
                var ticks = MsiApi.MsiRecordGetInteger(hRecord, 2);
                _completed += _forward ? ticks : -ticks;
                break;
            case 3: // add ticks to total
                _total += MsiApi.MsiRecordGetInteger(hRecord, 2);
                break;
        }
        Report(null, null);
        return _cancel ? MsiApi.IDCANCEL : MsiApi.IDOK;
    }

    private int _lastPercent;
    private bool _holdPercent;

    private void Report(string? action, string? detail)
    {
        int percent;
        if (_holdPercent || _total <= 0)
        {
            percent = _lastPercent;
        }
        else
        {
            var raw = Math.Max(0.0, Math.Min(1.0, (double)_completed / _total));
            // Script generation is quick and changes nothing on disk: map it to the first 5%.
            percent = _inScript ? (int)(raw * 5) : 5 + (int)(raw * 95);
        }
        _lastPercent = percent;
        Progress?.Invoke(new MsiProgress(percent, action, detail));
    }
}
