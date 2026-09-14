using System.IO;
using System.Windows;

namespace FullFeature;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var config = Path.Combine(AppContext.BaseDirectory, "app.settings.json");
        var settings = File.Exists(config) ? File.ReadAllText(config) : "(no app.settings.json written by the setup hook)";
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        InfoText.Text = $"Running from {AppContext.BaseDirectory}\n\nSettings: {settings}\n\nArguments: {string.Join(" ", args)}";
    }
}
