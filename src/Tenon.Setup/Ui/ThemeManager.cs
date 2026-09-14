using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Tenon.Payload;

namespace Tenon.Setup.Ui;

/// <summary>Applies the manifest theme (accent, light/dark) to the application resources at startup.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(Application app, ManifestUi ui)
    {
        IsDark = ui.DarkMode switch
        {
            "dark" => true,
            "light" => false,
            _ => SystemPrefersDark(),
        };

        var accent = ParseColor(ui.Accent, Color.FromRgb(0x25, 0x63, 0xEB));
        var res = app.Resources;
        res["AccentColor"] = accent;
        res["AccentBrush"] = Freeze(new SolidColorBrush(accent));
        res["AccentHoverBrush"] = Freeze(new SolidColorBrush(Lighten(accent, IsDark ? 0.12 : -0.08)));
        res["AccentPressedBrush"] = Freeze(new SolidColorBrush(Lighten(accent, IsDark ? 0.22 : -0.16)));
        res["AccentForegroundBrush"] = Freeze(new SolidColorBrush(Luminance(accent) > 0.6 ? Colors.Black : Colors.White));
        res["SidePanelBrush"] = Freeze(new LinearGradientBrush(accent, Lighten(accent, -0.25), 90));

        if (IsDark)
        {
            Set(res, "WindowBackgroundBrush", 0x1F, 0x1F, 0x23);
            Set(res, "ContentBackgroundBrush", 0x1F, 0x1F, 0x23);
            Set(res, "FooterBackgroundBrush", 0x18, 0x18, 0x1C);
            Set(res, "ForegroundBrush", 0xF3, 0xF4, 0xF6);
            Set(res, "MutedForegroundBrush", 0x9C, 0xA3, 0xAF);
            Set(res, "ControlBackgroundBrush", 0x2A, 0x2A, 0x30);
            Set(res, "ControlBorderBrush", 0x3F, 0x3F, 0x46);
            Set(res, "ControlHoverBrush", 0x35, 0x35, 0x3C);
            Set(res, "TrackBrush", 0x35, 0x35, 0x3C);
        }
        else
        {
            Set(res, "WindowBackgroundBrush", 0xFF, 0xFF, 0xFF);
            Set(res, "ContentBackgroundBrush", 0xFF, 0xFF, 0xFF);
            Set(res, "FooterBackgroundBrush", 0xF6, 0xF7, 0xF9);
            Set(res, "ForegroundBrush", 0x11, 0x18, 0x27);
            Set(res, "MutedForegroundBrush", 0x6B, 0x72, 0x80);
            Set(res, "ControlBackgroundBrush", 0xFF, 0xFF, 0xFF);
            Set(res, "ControlBorderBrush", 0xD1, 0xD5, 0xDB);
            Set(res, "ControlHoverBrush", 0xF3, 0xF4, 0xF6);
            Set(res, "TrackBrush", 0xE5, 0xE7, 0xEB);
        }

        if (!string.IsNullOrEmpty(ui.Background))
        {
            var bg = ParseColor(ui.Background!, (Color)res["AccentColor"]);
            res["WindowBackgroundBrush"] = Freeze(new SolidColorBrush(bg));
            res["ContentBackgroundBrush"] = Freeze(new SolidColorBrush(bg));
        }

        if (!string.IsNullOrEmpty(ui.Font))
            res["SetupFontFamily"] = new FontFamily(ui.Font + ", Segoe UI Variable Text, Segoe UI");
    }

    private static void Set(ResourceDictionary res, string key, byte r, byte g, byte b)
        => res[key] = Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static Color ParseColor(string text, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch
        {
            return fallback;
        }
    }

    private static Color Lighten(Color c, double amount)
    {
        byte Ch(byte v) => (byte)Math.Max(0, Math.Min(255, v + amount * 255));
        return Color.FromRgb(Ch(c.R), Ch(c.G), Ch(c.B));
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
