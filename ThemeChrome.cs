using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace XenoStudio
{
    /// <summary>
    /// The parts of the window the web UI can't paint: the Windows title bar, the window border, and the background
    /// shown for a moment before the page loads. Each theme's colors here must match its CSS in web\style.css
    /// (--bg for Background, title text = the theme's caption ink, border = its accent).
    /// </summary>
    static class ThemeChrome
    {
        sealed record Palette(string Background, string Caption, string CaptionText, string Border, bool Dark);

        static readonly System.Collections.Generic.Dictionary<string, Palette> Themes = new()
        {
            ["dark"] = new("#1e1f22", "#1e1f22", "#d4d4d4", "#2e3035", true),
            ["light"] = new("#ffffff", "#f3f3f3", "#1f1f1f", "#e1e1e1", false),
            ["cyberpunk"] = new("#0b0618", "#0b0618", "#05d9e8", "#ff2a6d", true),   // night-city purple, cyan title, neon-pink edge
            ["hacker"] = new("#020602", "#020602", "#33ff66", "#1f8f3a", true),      // black terminal, phosphor-green title and edge
        };

        public static void Apply(Window w, Microsoft.Web.WebView2.Wpf.WebView2 web, string theme)
        {
            var p = Themes.TryGetValue(theme ?? "", out var t) ? t : Themes["dark"];
            var bg = (Color)ColorConverter.ConvertFromString(p.Background);
            w.Background = new SolidColorBrush(bg);
            web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);

            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = p.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            // Windows 11 only; on Windows 10 these calls simply fail and the title bar stays default
            int caption = ColorRef(p.Caption), text = ColorRef(p.CaptionText), border = ColorRef(p.Border);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        /// <summary>#RRGGBB → COLORREF (0x00BBGGRR).</summary>
        static int ColorRef(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return c.R | (c.G << 8) | (c.B << 16);
        }

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
