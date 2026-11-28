using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ScheduleLoader;

/// <summary>Look and feel: deep forest greens, cream text and one bright green accent, taken from the mod art.</summary>
static class Ui
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Brush B(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }

    public static readonly Brush Bg = B("#060b07"), Card = B("#0d1610"), Soft = B("#15211a"), Edge = B("#1d3024"), Hover = B("#2f6b3a"),
        Fg = B("#f2ecc6"), Dim = B("#8ea592"), Accent = B("#7ddc54"), Amber = B("#ffb84d"), Red = B("#ff6b5e"), Ink = B("#06100a");
    public static readonly Brush Grad = new LinearGradientBrush(C("#9be85f"), C("#34b04a"), 0);

    // soft green glow behind the top of every page
    public static Brush Backdrop()
    {
        var b = new RadialGradientBrush { Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 1, RadiusY = 0.7 };
        b.GradientStops.Add(new GradientStop(C("#123220"), 0));
        b.GradientStops.Add(new GradientStop(C("#060b07"), 1));
        return b;
    }

    public static Effect Glow(double blur = 18, double opacity = 0.35) => new DropShadowEffect { Color = C("#7ddc54"), BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };

    public static TextBlock Tx(string t, double size, Brush c, bool bold = false) =>
        new() { Text = t, FontSize = size, Foreground = c, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    // fade + slide up; used for pages, cards and popups
    public static void In(UIElement e, double dy = 14, int delayMs = 0)
    {
        var move = new TranslateTransform(0, dy);
        e.RenderTransform = move;
        e.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(300);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { BeginTime = begin, EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, time) { BeginTime = begin, EasingFunction = ease });
    }

    public static Border Btn(string t, Brush bg, Brush fg, Action click, bool outline = false)
    {
        var scale = new ScaleTransform(1, 1);
        var b = new Border
        {
            Background = outline ? Brushes.Transparent : bg, BorderBrush = Edge, BorderThickness = new Thickness(outline ? 1 : 0),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 9, 18, 9), Cursor = Cursors.Hand,
            RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = ReferenceEquals(bg, Grad) ? Glow(16, 0.3) : null,
            Child = new TextBlock { Text = t, Foreground = fg, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center }
        };
        void To(double s)
        {
            var a = new DoubleAnimation(s, TimeSpan.FromMilliseconds(120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        b.MouseEnter += (_, _) => To(1.04);
        b.MouseLeave += (_, _) => To(1);
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // keeps window dragging from eating clicks
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    // keeps its own on/off state and animates, so callers don't need to redraw
    public static Border Toggle(bool on, bool locked, Action<bool> set)
    {
        var slide = new TranslateTransform(on ? 22 : 0, 0);
        var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(3), Background = on ? Ink : Dim, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = slide };
        var pill = new Border { Width = 46, Height = 24, CornerRadius = new CornerRadius(12), Background = on ? Grad : Soft, Child = knob, Opacity = locked ? 0.5 : 1, Cursor = locked ? Cursors.Arrow : Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        if (!locked)
            pill.MouseLeftButtonUp += (_, _) =>
            {
                on = !on;
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 22 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() });
                pill.Background = on ? Grad : Soft;
                knob.Background = on ? Ink : Dim;
                set(on);
            };
        return pill;
    }

    // ---------- thumbnails: use the picture if it loads, otherwise fall back to the mod's name ----------
    static readonly Dictionary<string, ImageSource?> Images = new();
    public static void ForgetImages() => Images.Clear(); // thumbnails changed on disk (online catalog)

    static ImageSource? Img(string? file)
    {
        if (file == null) return null;
        if (Images.TryGetValue(file, out var cached)) return cached;
        ImageSource? src = null;
        try
        {
            var path = Path.Combine(Paths.App, "Assets", file);
            if (File.Exists(path))
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.DecodePixelWidth = 400;
                b.UriSource = new Uri(path);
                b.EndInit();
                b.Freeze();
                src = b;
            }
            else Logger.Write("Thumbnail missing: " + path);
        }
        catch (Exception ex) { Logger.Write($"Thumbnail failed ({file}): {ex.Message}"); }
        return Images[file] = src;
    }

    // the SL logo, or nothing if the file is missing
    public static Image? Logo(double size)
    {
        var img = Img("logo.png");
        return img == null ? null : new Image { Source = img, Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
    }

    public static Border Thumb(ModInfo m)
    {
        var box = new Border { Width = 168, Height = 96, CornerRadius = new CornerRadius(14), Background = Brushes.Black, BorderBrush = Edge, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
        var img = Img(m.Thumb);
        if (img != null) box.Background = new ImageBrush(img) { Stretch = Stretch.Uniform };
        else box.Child = new TextBlock { Text = m.Name, Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
        return box;
    }
}
