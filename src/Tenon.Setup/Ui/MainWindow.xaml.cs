using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Tenon.Setup.Ui;

public partial class MainWindow : Window
{
    public static readonly IValueConverter Not = new NotConverter();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyRoundedCorners();
        DataContextChanged += (_, _) => Bind();
    }

    private void Bind()
    {
        if (DataContext is not SetupViewModel vm) return;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(SetupViewModel.Page)) LoadLicense(vm);
        };
        LoadLicense(vm);

        if (vm.LogoBytes != null)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(vm.LogoBytes);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                Logo.Source = image;
                TitleIcon.Source = image;
            }
            catch
            {
                // ignore bad images
            }
        }
        else
        {
            try
            {
                var hIcon = ExtractIconW(IntPtr.Zero, Environment.ProcessPath!, 0);
                if (hIcon != IntPtr.Zero && hIcon != new IntPtr(1))
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    Logo.Source = source;
                    TitleIcon.Source = source;
                    DestroyIcon(hIcon);
                }
            }
            catch
            {
                // no icon
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string exeFileName, int iconIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private bool _licenseLoaded;

    private void LoadLicense(SetupViewModel vm)
    {
        if (_licenseLoaded || !vm.HasLicense || !vm.IsLicense) return;
        _licenseLoaded = true;
        var range = new TextRange(LicenseBox.Document.ContentStart, LicenseBox.Document.ContentEnd);
        try
        {
            using var stream = new MemoryStream(vm.LicenseIsRtf ? System.Text.Encoding.ASCII.GetBytes(vm.LicenseText) : System.Text.Encoding.UTF8.GetBytes(vm.LicenseText));
            range.Load(stream, vm.LicenseIsRtf ? DataFormats.Rtf : DataFormats.Text);
        }
        catch
        {
            range.Text = vm.LicenseText;
        }
        if (vm.LicenseIsRtf)
        {
            // RTF carries its own colors; make sure the text stays readable in dark mode.
            range.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("ForegroundBrush"));
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel vm) vm.CancelCommand.Execute(null);
        else Close();
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
            var dark = ThemeManager.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch
        {
            // older Windows: no rounded corners
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
    }
}
