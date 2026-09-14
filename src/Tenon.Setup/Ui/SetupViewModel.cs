using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Tenon.Setup.Engine;

namespace Tenon.Setup.Ui;

public enum SetupPage { Welcome, Maintenance, License, Folder, Options, Progress, Finish, Error }

public sealed class TaskOption : ObservableObject
{
    private bool _isChecked;
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Drives the wizard: page flow, user choices and the install/uninstall run.</summary>
public sealed class SetupViewModel : ObservableObject
{
    private readonly SetupEngine _engine;
    private readonly Action<int> _exit;
    private readonly List<SetupPage> _flow = new();
    private int _flowIndex;
    private CancellationTokenSource? _cts;
    private SetupResult? _result;
    private bool _uninstalling;

    public SetupViewModel(SetupEngine engine, Action<int> exit)
    {
        _engine = engine;
        _exit = exit;
        var m = engine.Manifest;

        ProductName = m.ProductName;
        Version = m.Version;
        Publisher = m.Publisher;
        Description = m.Description ?? "";
        WindowTitle = $"{m.ProductName} Setup";
        InstallDir = engine.InstallDir;
        PerMachine = engine.PerMachine;
        LaunchApp = m.RunAfterInstall && !string.IsNullOrEmpty(m.MainExe);
        foreach (var t in m.Tasks)
            Tasks.Add(new TaskOption { Id = t.Id, Title = t.Title, Description = t.Description, IsChecked = engine.SelectedTasks.Contains(t.Id) });

        HasLicense = m.Ui.LicenseEntry != null && engine.HasEntry(m.Ui.LicenseEntry);
        if (HasLicense)
        {
            var bytes = engine.ReadEntry(m.Ui.LicenseEntry!);
            LicenseIsRtf = m.Ui.LicenseEntry!.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase);
            LicenseText = LicenseIsRtf ? System.Text.Encoding.ASCII.GetString(bytes) : System.Text.Encoding.UTF8.GetString(bytes);
        }
        if (m.Ui.LogoEntry != null && engine.HasEntry(m.Ui.LogoEntry)) LogoBytes = engine.ReadEntry(m.Ui.LogoEntry);

        NextCommand = new RelayCommand(Next, () => CanGoNext);
        BackCommand = new RelayCommand(Back, () => CanGoBack);
        CancelCommand = new RelayCommand(Cancel);
        BrowseCommand = new RelayCommand(Browse);
        OpenLogCommand = new RelayCommand(OpenLog);
        UninstallCommand = new RelayCommand(() => StartOperation(uninstall: true));
        RepairCommand = new RelayCommand(() => StartOperation(uninstall: false, repair: true));
        InstallCommand = new RelayCommand(() => StartOperation(uninstall: false));
    }

    // ------------------------------------------------------------ header
    public string ProductName { get; }
    public string Version { get; }
    public string Publisher { get; }
    public string Description { get; }
    public string WindowTitle { get; }
    public byte[]? LogoBytes { get; }

    // ------------------------------------------------------------ page state
    private SetupPage _page;
    public SetupPage Page
    {
        get => _page;
        private set
        {
            _page = value;
            // Always notify: the first page equals the enum default, and every page changes titles and buttons.
            RaiseAll();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Title => Page switch
    {
        SetupPage.Welcome => _engine.IsUpgrade ? $"Update to version {Version}" : $"Welcome to {ProductName}",
        SetupPage.Maintenance => $"{ProductName} is already installed",
        SetupPage.License => "License agreement",
        SetupPage.Folder => "Choose where to install",
        SetupPage.Options => "Options",
        SetupPage.Progress => _uninstalling ? $"Uninstalling {ProductName}" : $"Installing {ProductName}",
        SetupPage.Finish => _uninstalling ? "Uninstall complete" : (_result?.RebootRequired == true ? "Restart required" : "Installation complete"),
        SetupPage.Error => _result?.Cancelled == true ? "Setup was cancelled" : "Something went wrong",
        _ => "",
    };

    public string Subtitle => Page switch
    {
        SetupPage.Welcome => _engine.IsUpgrade
            ? $"Version {_engine.Installed!.VersionString} is installed. Setup will update it to {Version} and keep your settings."
            : $"This wizard installs {ProductName} {Version} on your computer.",
        SetupPage.Maintenance => $"Version {_engine.Installed?.VersionString} is installed. Choose what you want to do.",
        SetupPage.License => "Please read the following license agreement. You must accept it to continue.",
        SetupPage.Folder => "Setup will install the application into the folder below.",
        SetupPage.Options => "Select the additional tasks you would like Setup to perform.",
        SetupPage.Progress => "Please wait while Setup completes. This may take a few minutes.",
        SetupPage.Finish => _uninstalling
            ? $"{ProductName} has been removed from your computer."
            : (_result?.RebootRequired == true ? "Restart your computer to finish the installation." : $"{ProductName} {Version} is ready to use."),
        SetupPage.Error => _result?.Cancelled == true ? "No changes were made to your computer." : "Setup could not complete the installation.",
        _ => "",
    };

    public bool IsWelcome => Page == SetupPage.Welcome;
    public bool IsMaintenance => Page == SetupPage.Maintenance;
    public bool IsLicense => Page == SetupPage.License;
    public bool IsFolder => Page == SetupPage.Folder;
    public bool IsOptions => Page == SetupPage.Options;
    public bool IsProgress => Page == SetupPage.Progress;
    public bool IsFinish => Page == SetupPage.Finish;
    public bool IsError => Page == SetupPage.Error;

    // ------------------------------------------------------------ license
    public bool HasLicense { get; }
    public bool LicenseIsRtf { get; }
    public string LicenseText { get; } = "";
    private bool _licenseAccepted;
    public bool LicenseAccepted { get => _licenseAccepted; set { Set(ref _licenseAccepted, value); Raise(nameof(CanGoNext)); } }

    // ------------------------------------------------------------ folder and scope
    private string _installDir = "";
    public string InstallDir
    {
        get => _installDir;
        set
        {
            if (Set(ref _installDir, value)) { _engine.InstallDir = value; Raise(nameof(SpaceInfo)); Raise(nameof(CanGoNext)); }
        }
    }

    public bool AllowChangeDir => _engine.Manifest.AllowChangeDir && !_engine.IsInstalled;
    public bool CanChooseScope => _engine.CanChooseScope;

    private bool _perMachine;
    public bool PerMachine
    {
        get => _perMachine;
        set
        {
            if (!Set(ref _perMachine, value)) return;
            _engine.PerMachine = value;
            if (!_engine.IsInstalled) InstallDir = FolderResolver.Resolve(_engine.Manifest.InstallDir, value, _engine.Manifest.Arch);
            Raise(nameof(PerUser));
            Raise(nameof(ScopeNote));
        }
    }

    public bool PerUser { get => !PerMachine; set => PerMachine = !value; }
    public string ScopeNote => PerMachine && !SetupEngine.IsProcessElevated ? "Installing for all users requires administrator permission. Windows will ask for it." : "";

    public string SpaceInfo
    {
        get
        {
            try
            {
                var root = Path.GetPathRoot(InstallDir);
                if (string.IsNullOrEmpty(root)) return "";
                var drive = new DriveInfo(root);
                return $"{drive.AvailableFreeSpace / 1024 / 1024:N0} MB free on {root.TrimEnd('\\')}";
            }
            catch
            {
                return "";
            }
        }
    }

    // ------------------------------------------------------------ options
    public ObservableCollection<TaskOption> Tasks { get; } = new();
    public bool HasTasks => Tasks.Count > 0;

    // ------------------------------------------------------------ progress
    private int _percent;
    public int Percent { get => _percent; private set => Set(ref _percent, value); }
    private string _status = "Preparing";
    public string Status { get => _status; private set => Set(ref _status, value); }
    private string? _detail;
    public string? Detail { get => _detail; private set => Set(ref _detail, value); }
    private bool _isIndeterminate = true;
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
    public bool IsBusy => Page == SetupPage.Progress;

    // ------------------------------------------------------------ finish / error
    private bool _launchApp;
    public bool LaunchApp { get => _launchApp; set => Set(ref _launchApp, value); }
    public bool CanLaunch => !_uninstalling && !string.IsNullOrEmpty(_engine.Manifest.MainExe) && _engine.Manifest.RunAfterInstall && _result?.RebootRequired != true;
    public string LaunchText => $"Launch {ProductName} now";
    public bool RebootRequired => _result?.RebootRequired == true;
    public string ErrorMessage => _result?.ErrorMessage ?? "";
    public string? ErrorHint => _result?.Hint;
    public string LogPath => _engine.Log.Path;
    public string InstalledVersion => _engine.Installed?.VersionString ?? "";
    public bool CanRepair => !_engine.Manifest.ArpNoRepair;
    public bool CanDeleteAppData => _engine.CanDeleteAppData;
    private bool _deleteAppData;
    public bool DeleteAppData { get => _deleteAppData; set { Set(ref _deleteAppData, value); _engine.DeleteAppData = value; } }
    public string ElevationNote => _engine.NeedsElevationForInstall || _engine.NeedsElevationForUninstall ? "Windows will ask for administrator permission." : "";

    // ------------------------------------------------------------ navigation
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand InstallCommand { get; }

    public bool ShowBack => Page is SetupPage.License or SetupPage.Folder or SetupPage.Options;
    public bool ShowNext => Page is SetupPage.Welcome or SetupPage.License or SetupPage.Folder or SetupPage.Options or SetupPage.Finish or SetupPage.Error;
    public bool ShowCancel => Page is not (SetupPage.Finish or SetupPage.Error);
    public string NextButtonText => Page switch
    {
        SetupPage.Finish or SetupPage.Error => "Finish",
        SetupPage.Welcome when _flow.Count <= 2 => _engine.IsUpgrade ? "Update" : "Install",
        _ when _flowIndex + 1 < _flow.Count && _flow[_flowIndex + 1] == SetupPage.Progress => _engine.IsUpgrade ? "Update" : "Install",
        _ => "Next",
    };

    public bool CanGoBack => ShowBack && _flowIndex > 0;
    public bool CanGoNext => Page switch
    {
        SetupPage.License => LicenseAccepted,
        SetupPage.Folder => !string.IsNullOrWhiteSpace(InstallDir) && Path.IsPathRooted(InstallDir),
        SetupPage.Progress => false,
        _ => true,
    };

    /// <summary>Called once the window is shown.</summary>
    public void Start()
    {
        var args = _engine.Args;
        if (_engine.IsDowngrade && args.Mode is SetupMode.Install or SetupMode.Update)
        {
            _result = new SetupResult { ExitCode = 1638, ErrorMessage = $"A newer version ({_engine.Installed!.VersionString}) of {ProductName} is already installed.", Hint = "Uninstall it first if you want to install this older version." };
            Page = SetupPage.Error;
            return;
        }

        if (args.Mode == SetupMode.Uninstall)
        {
            if (!_engine.IsInstalled) { _result = new SetupResult { ExitCode = 0 }; _uninstalling = true; Page = SetupPage.Finish; return; }
            if (args.Passive) { StartOperation(uninstall: true); return; }
            _flow.AddRange(new[] { SetupPage.Maintenance, SetupPage.Progress });
            Page = SetupPage.Maintenance;
            return;
        }

        if (_engine.IsSameVersion && args.Mode != SetupMode.Update)
        {
            _flow.AddRange(new[] { SetupPage.Maintenance, SetupPage.Progress });
            Page = SetupPage.Maintenance;
            return;
        }

        BuildInstallFlow();
        _engine.Log.Info("Page flow: " + string.Join(" > ", _flow));
        if (args.Passive || _engine.Manifest.Ui.Style == "oneClick")
        {
            StartOperation(uninstall: false);
            return;
        }
        _flowIndex = 0;
        Page = _flow[0];
    }

    private void BuildInstallFlow()
    {
        _flow.Clear();
        var pages = _engine.Manifest.Ui.Pages.Select(p => p.ToLowerInvariant()).ToList();
        if (pages.Count == 0) pages = new() { "welcome", "license", "folder", "options", "progress", "finish" };
        foreach (var p in pages)
        {
            switch (p)
            {
                case "welcome": _flow.Add(SetupPage.Welcome); break;
                case "license": if (HasLicense) _flow.Add(SetupPage.License); break;
                case "scope":
                case "folder": if (!_engine.IsInstalled && (AllowChangeDir || CanChooseScope)) _flow.Add(SetupPage.Folder); break;
                case "options": if (HasTasks) _flow.Add(SetupPage.Options); break;
            }
        }
        if (!_flow.Contains(SetupPage.Welcome)) _flow.Insert(0, SetupPage.Welcome);
        _flow.Add(SetupPage.Progress);
    }

    private void Next()
    {
        if (Page is SetupPage.Finish or SetupPage.Error)
        {
            if (Page == SetupPage.Finish && _engine.Args.RestartApp != null) _engine.RestartAppIfRequested();
            else if (Page == SetupPage.Finish && LaunchApp && CanLaunch) _engine.LaunchApp();
            _exit(_result?.ExitCode ?? 0);
            return;
        }
        if (_flowIndex + 1 >= _flow.Count) return;
        _flowIndex++;
        if (_flow[_flowIndex] == SetupPage.Progress) StartOperation(uninstall: false);
        else Page = _flow[_flowIndex];
    }

    private void Back()
    {
        if (_flowIndex == 0) return;
        _flowIndex--;
        Page = _flow[_flowIndex];
    }

    private void Cancel()
    {
        if (Page == SetupPage.Progress)
        {
            if (MessageBox.Show("Cancel the installation? Changes made so far will be rolled back.", WindowTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Status = "Cancelling";
                _cts?.Cancel();
            }
            return;
        }
        _exit(1602);
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the installation folder", InitialDirectory = Directory.Exists(InstallDir) ? InstallDir : Path.GetDirectoryName(InstallDir) };
        if (dialog.ShowDialog() == true)
        {
            var chosen = dialog.FolderName;
            var leaf = Path.GetFileName(_installDir.TrimEnd('\\'));
            InstallDir = string.Equals(Path.GetFileName(chosen), leaf, StringComparison.OrdinalIgnoreCase) ? chosen : Path.Combine(chosen, leaf);
        }
    }

    private void OpenLog()
    {
        try { Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true }); } catch { /* ignore */ }
    }

    private async void StartOperation(bool uninstall, bool repair = false)
    {
        _uninstalling = uninstall;
        foreach (var t in Tasks)
        {
            if (t.IsChecked) _engine.SelectedTasks.Add(t.Id); else _engine.SelectedTasks.Remove(t.Id);
        }
        _engine.InstallDir = InstallDir;
        _engine.PerMachine = PerMachine;

        Page = SetupPage.Progress;
        Percent = 0;
        IsIndeterminate = true;
        Status = "Preparing";
        _cts = new CancellationTokenSource();
        var progress = new Progress<SetupProgress>(p =>
        {
            if (p.Percent > 0) IsIndeterminate = false;
            Percent = Math.Max(Percent, p.Percent);
            Status = p.Status;
            Detail = p.Detail;
        });

        try
        {
            _result = uninstall
                ? await _engine.UninstallAsync(progress, _cts.Token)
                : repair ? await _engine.RepairAsync(progress, _cts.Token)
                : await _engine.InstallAsync(progress, _cts.Token);
        }
        catch (Exception ex)
        {
            _engine.Log.Error(ex.ToString());
            _result = new SetupResult { ExitCode = 1603, ErrorMessage = ex.Message };
        }

        _engine.Log.Info($"Finished with exit code {_result.ExitCode}");
        if (_result.Succeeded)
        {
            Percent = 100;
            IsIndeterminate = false;
            Page = SetupPage.Finish;
            if (_engine.Args.Passive)
            {
                _engine.RestartAppIfRequested();
                _exit(_result.ExitCode);
            }
        }
        else
        {
            Page = SetupPage.Error;
            if (_engine.Args.Passive) _exit(_result.ExitCode);
        }
    }
}
