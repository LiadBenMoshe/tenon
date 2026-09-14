using System.Reflection;
using System.Windows;
using Tenon.Update;

namespace HelloWpf;

public partial class MainWindow : Window
{
    private readonly UpdateManager _updates = UpdateManager.FromInstalledApp();
    private DownloadedUpdate? _downloaded;

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        InfoText.Text = $"Version {version}\nRunning from {AppContext.BaseDirectory}";
        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!_updates.IsInstalled)
        {
            UpdateText.Text = "Not installed by Tenon (running from a build folder); update checks are disabled.";
            return;
        }
        try
        {
            UpdateText.Text = "Checking for updates...";
            var update = await _updates.CheckForUpdatesAsync();
            if (update == null)
            {
                UpdateText.Text = "You are up to date.";
                return;
            }
            UpdateText.Text = $"Version {update.VersionString} is available. Downloading...";
            _downloaded = await _updates.DownloadUpdatesAsync(update, new Progress<UpdateProgress>(p => UpdateText.Text = $"Downloading {update.VersionString}: {p.Percent:F0}%"));
            UpdateText.Text = $"Version {update.VersionString} is ready to install.";
            UpdateButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Update check failed: " + ex.Message;
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloaded != null) _updates.ApplyUpdatesAndRestart(_downloaded);
    }
}
