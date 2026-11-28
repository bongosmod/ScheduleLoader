using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ScheduleLoader;

public class MainWindow : Window
{
    const string GitHub = "https://github.com/bongosmod/ScheduleLoader";
    const string Version = "V0.1.3_BETA";

    readonly Grid _stage = new();
    readonly StackPanel _list = new(), _tabs = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    Border _buttons = new(), _pop = new(), _primary = new();
    TextBlock _status = new();
    string _tab = "Library";
    bool _dirty;

    public MainWindow()
    {
        Title = "Schedule Loader";
        Width = 640; Height = 720;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed && e.GetPosition(this).Y < 70) try { DragMove(); } catch { } };
        Content = new Border
        {
            Margin = new Thickness(14), CornerRadius = new CornerRadius(20), Background = Ui.Backdrop(), BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.7 }, Child = _stage
        };
        ShowWelcome();
        Splash();
        // started once the window is up: from the constructor the code after the download ran off the UI thread and crashed
        Loaded += (_, _) => LoadOnline();
    }

    // the online mod list (newest mods and versions): when it changes anything, redraw the mod cards
    async void LoadOnline()
    {
        if (!await Online.RefreshAsync()) return;
        if (_list.Children.Count > 0) Render(); // only once the mod page has been shown
    }

    // fade the current page out, then build the next one (which fades in)
    async void GoTo(Action show)
    {
        if (_stage.Children.Count > 0)
        {
            _stage.Children[0].BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)));
            await Task.Delay(170);
        }
        show();
    }

    // ---------- loading screen ----------
    void Splash()
    {
        var s = new Grid { Background = Ui.Bg };
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var title = Ui.Tx("SCHEDULE LOADER", 34, Ui.Fg, true);
        title.Effect = Ui.Glow(26, 0.4);
        var line = new Border { Height = 3, Width = 0, CornerRadius = new CornerRadius(2), Background = Ui.Grad, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8), Effect = Ui.Glow(12, 0.6) };
        var logo = Ui.Logo(120);
        if (logo != null) st.Children.Add(logo);
        st.Children.Add(title);
        st.Children.Add(line);
        st.Children.Add(Ui.Tx("by Bongosmod", 13, Ui.Dim));
        s.Children.Add(st);
        _stage.Children.Add(s);

        line.BeginAnimation(WidthProperty, new DoubleAnimation(0, 320, TimeSpan.FromSeconds(1.5)) { EasingFunction = new CubicEase() });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(1.8) };
        fade.Completed += (_, _) => _stage.Children.Remove(s);
        s.BeginAnimation(OpacityProperty, fade);
    }

    // ---------- welcome ----------
    void ShowWelcome()
    {
        _stage.Children.Clear();
        _status = Ui.Tx("", 12, Ui.Dim); _status.TextAlignment = TextAlignment.Center; _status.Margin = new Thickness(0, 16, 0, 16); _status.MinHeight = 64;

        var hi = Ui.Tx("Welcome To", 20, Ui.Dim);
        var big = Ui.Tx("Schedule Loader", 44, Ui.Grad, true); big.Effect = Ui.Glow(28, 0.45);
        var by = Ui.Tx("By Bongosmod", 12, Ui.Dim);
        var col = new StackPanel { Width = 410, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var t in new[] { hi, big, by }) { t.TextAlignment = TextAlignment.Center; col.Children.Add(t); }

        var box = new StackPanel();
        _primary = Ui.Btn("", Ui.Grad, Ui.Ink, OpenOrSetup);
        RefreshPrimary();
        box.Children.Add(_primary);
        var up = Ui.Btn("Upload Mods", Ui.Soft, Ui.Fg, () => { if (Upload()) GoTo(ShowMain); }, outline: true);
        up.Margin = new Thickness(0, 10, 0, 0);
        box.Children.Add(up);
        box.Children.Add(_status);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Ui.Btn("Open GitHub Page", Ui.Soft, Ui.Dim, () => Process.Start(new ProcessStartInfo(GitHub) { UseShellExecute = true })));
        var log = Ui.Btn("Open Log", Ui.Soft, Ui.Dim, Logger.Open, outline: true);
        log.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(log, 1);
        row.Children.Add(log);
        var un = Ui.Btn("Uninstall", Ui.Soft, Ui.Red, Uninstall, outline: true);
        un.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(un, 2);
        row.Children.Add(un);
        box.Children.Add(row);

        col.Children.Add(new Border { Background = Ui.Card, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(22), Margin = new Thickness(0, 26, 0, 0), Child = box });

        var page = new Grid();
        page.Children.Add(col);
        page.Children.Add(Close(Ui.Fg));
        _stage.Children.Add(page);
        Ui.In(page, 16);
    }

    void RefreshPrimary() => ((TextBlock)_primary.Child).Text = Setup.Done ? "Open Schedule Loader" : "Set up Schedule Loader";

    // Opening also checks for updates; it is quick when everything is already current.
    void OpenOrSetup()
    {
        if (Setup.Done && Game.Running != null) GoTo(ShowMain); // game is open: skip the checks
        else RunSetup(_primary);
    }

    async void RunSetup(Border btn)
    {
        if (!Melon.Installed && Game.Found && MessageBox.Show(
                $"Schedule Loader will install MelonLoader (the official open-source mod loader) into:\n\n{Game.Dir}\n\nYour save files are not touched, and everything is undone automatically if the install fails.\n\nContinue?",
                "Schedule Loader", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        btn.IsHitTestVisible = false; btn.Opacity = 0.5;
        try
        {
            var warn = await Setup.RunAsync(s => { Logger.Write(s); Say(s, Ui.Amber); });
            if (!Setup.Done) throw new InvalidOperationException(warn ?? "Set up did not finish.");
            Say(warn ?? "All set!", warn == null ? Ui.Accent : Ui.Amber);
            await Task.Delay(warn == null ? 700 : 2500);
            GoTo(ShowMain);
        }
        catch (Exception ex)
        {
            Logger.Write("Setup failed: " + ex);
            Say(ex.Message + "\n(Details: " + Logger.LogPath + ")", Ui.Red);
            btn.IsHitTestVisible = true; btn.Opacity = 1;
        }
    }

    void Uninstall()
    {
        if (!Game.Found) { Say("Schedule I not found.", Ui.Red); return; }
        if (MessageBox.Show($"This removes everything Schedule Loader installed:\n\n• MelonLoader, S1API, S1MAPI and every mod in {Game.Dir}\n• every mod in your Library, including Better Stackables, TV+ and custom uploads\n• the mods' settings, including your TV+ wallpaper\n\nYour saves are not touched. To use mods again, click Set up, then install the ones you want from the Browser.\n\nContinue?",
                "Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { Say(Setup.Uninstall(), Ui.Accent); RefreshPrimary(); Render(); }
        catch (Exception ex) { Logger.Write("Uninstall failed: " + ex); Say(ex.Message, Ui.Red); }
    }

    bool Upload()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Mods (*.dll, *.zip)|*.dll;*.zip", Multiselect = true, Title = "Choose mods to add (a .dll, or the .zip you downloaded)" };
        if (d.ShowDialog() != true) return false;
        if (MessageBox.Show("Custom mods run with full access to your PC. Only add mods you trust.\n\nContinue?", "Schedule Loader", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        var report = new List<string>();
        foreach (var f in d.FileNames)
            try { report.AddRange(Uploads.Add(f)); }
            catch (Exception ex) { Logger.Write($"Upload of {f} failed: {ex}"); report.Add($"{Path.GetFileName(f)}: could not be added ({ex.Message})."); }
        Logger.Write("Upload: " + string.Join(" | ", report));
        MessageBox.Show(string.Join("\n\n", report) + "\n\nMods start with the game the next time you press Launch.", "Upload", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    // ---------- main: header, tabs, cards, footer ----------
    void ShowMain()
    {
        Store.Pull();
        _stage.Children.Clear();
        _status = Ui.Tx("", 12, Ui.Dim); _status.VerticalAlignment = VerticalAlignment.Center;

        var page = new Grid();
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            page.RowDefinitions.Add(new RowDefinition { Height = h });

        var title = Ui.Tx("Schedule Loader", 19, Ui.Ink, true);
        title.HorizontalAlignment = HorizontalAlignment.Center; title.VerticalAlignment = VerticalAlignment.Center;
        page.Children.Add(new Border { Background = Ui.Grad, Height = 58, CornerRadius = new CornerRadius(19, 19, 0, 0), Child = title });
        page.Children.Add(Close(Ui.Ink));

        // version on the left, page tabs on the right
        var ver = Ui.Tx(Version, 11, Ui.Dim, true);
        ver.HorizontalAlignment = HorizontalAlignment.Left; ver.VerticalAlignment = VerticalAlignment.Bottom; ver.Margin = new Thickness(26, 0, 0, 4);
        var tabHost = new Grid { Margin = new Thickness(0, 12, 24, 8) };
        tabHost.Children.Add(ver);
        tabHost.Children.Add(_tabs);
        Grid.SetRow(tabHost, 1);
        page.Children.Add(tabHost);

        var scroll = new ScrollViewer { Content = _list, Margin = new Thickness(22, 4, 22, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        page.Children.Add(scroll);

        var foot = new DockPanel { Margin = new Thickness(24, 14, 24, 18) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Btn("Upload Custom", Ui.Soft, Ui.Fg, () => { if (Upload()) Render(); }, outline: true));
        var go = Ui.Btn("Launch Game", Ui.Grad, Ui.Ink, Launch);
        go.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(go);
        _buttons = new Border { Child = row };
        DockPanel.SetDock(_buttons, Dock.Right);
        foot.Children.Add(_buttons);
        foot.Children.Add(_status);
        Grid.SetRow(foot, 3);
        page.Children.Add(foot);

        _stage.Children.Add(page);
        Ui.In(page, 16);
        Render();
    }

    void Render()
    {
        _tabs.Children.Clear();
        foreach (var name in new[] { "Library", "Browser" })
        {
            bool active = name == _tab;
            var t = new StackPanel { Margin = new Thickness(18, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            t.Children.Add(Ui.Tx(name, 14, active ? Ui.Fg : Ui.Dim, true));
            t.Children.Add(new Border { Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 4, 0, 0), Background = active ? Ui.Grad : Brushes.Transparent, Effect = active ? Ui.Glow(8, 0.7) : null });
            t.MouseLeftButtonUp += (_, _) => { if (_tab != name) { _tab = name; Render(); } };
            _tabs.Children.Add(t);
        }

        _buttons.Visibility = _tab == "Library" ? Visibility.Visible : Visibility.Collapsed;
        _list.Children.Clear();
        bool lib = _tab == "Library";
        int i = 0;
        foreach (var m in lib ? Store.Library() : Store.Catalog.Where(c => !c.Auto).ToList()) // the Browser only offers the optional downloads
        {
            var card = Card(m, lib);
            _list.Children.Add(card);
            Ui.In(card, 14, i++ * 50); // cards slide in one after another
        }
    }

    UIElement Card(ModInfo m, bool lib)
    {
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Ui.Tx(m.Name, 17, Ui.Fg, true));
        var by = new StackPanel { Orientation = Orientation.Horizontal };
        by.Children.Add(Ui.Tx("By: " + m.Author, 12, Ui.Dim));
        if (m.Custom) { var tag = Ui.Tx("Custom Upload", 12, Ui.Amber, true); tag.Margin = new Thickness(10, 0, 0, 0); by.Children.Add(tag); }
        info.Children.Add(by);
        if (!lib) { var d = Ui.Tx(m.Description, 12, Ui.Dim); d.Margin = new Thickness(0, 4, 0, 0); info.Children.Add(d); }
        info.Children.Add(Actions(m, lib, false));

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Ui.Thumb(m));
        Grid.SetColumn(info, 1);
        g.Children.Add(info);

        var card = new Border { Background = Ui.Card, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), Child = g };
        card.MouseEnter += (_, _) => card.BorderBrush = Ui.Hover;
        card.MouseLeave += (_, _) => card.BorderBrush = Ui.Edge;
        return card;
    }

    // Info + toggle (library) or Install (browser). The popup leaves the Info button out.
    UIElement Actions(ModInfo m, bool lib, bool popup)
    {
        var a = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        void Add(UIElement e) { if (e is FrameworkElement f) f.Margin = new Thickness(10, 0, 0, 0); a.Children.Add(e); }
        if (!popup) Add(Ui.Btn("Info", Ui.Soft, Ui.Fg, () => ShowInfo(m, lib)));
        if (popup && lib && m.Custom) Add(Ui.Btn("Remove", Ui.Soft, Ui.Red, () => { Store.Remove(m); _dirty = true; ClosePop(); }));
        if (lib) Add(Ui.Toggle(Store.On(m), m.Required, on => { Store.Set(m, on); if (popup) _dirty = true; }));
        else if (Store.Has(m)) Add(Ui.Btn("Installed", Ui.Soft, Ui.Dim, () => { }));
        else Add(Ui.Btn("Install", Ui.Grad, Ui.Ink, () => Install(m)));
        return a;
    }

    // ---------- info popup ----------
    void ShowInfo(ModInfo m, bool lib)
    {
        ClosePop();
        var info = new StackPanel { Margin = new Thickness(0, 0, 36, 0) };
        info.Children.Add(Ui.Tx(m.Name, 19, Ui.Fg, true));
        info.Children.Add(Ui.Tx("By: " + m.Author, 12, Ui.Fg));
        var d = Ui.Tx(m.Description, 12, Ui.Dim); d.Margin = new Thickness(0, 6, 0, 12); info.Children.Add(d);
        info.Children.Add(Ui.Tx($"v{m.Version}, " + (m.Verified ? "Verified" : "Unverified"), 12, m.Verified ? Ui.Accent : Ui.Amber, true));
        info.Children.Add(Actions(m, lib, true));

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Ui.Thumb(m));
        Grid.SetColumn(info, 1);
        g.Children.Add(info);
        var x = Ui.Btn("✕", Ui.Soft, Ui.Fg, ClosePop);
        x.HorizontalAlignment = HorizontalAlignment.Right; x.VerticalAlignment = VerticalAlignment.Top; x.Padding = new Thickness(10, 4, 10, 4);
        Grid.SetColumn(x, 1);
        g.Children.Add(x);

        var card = new Border { Background = Ui.Card, BorderBrush = Ui.Hover, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(18), Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center, Child = g, Effect = Ui.Glow(30, 0.18) };
        _pop = new Border { Background = new SolidColorBrush(Color.FromArgb(0xD0, 0, 0, 0)), CornerRadius = new CornerRadius(20), Child = card };
        _stage.Children.Add(_pop);
        _pop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        Ui.In(card, 12);
    }

    void ClosePop()
    {
        _stage.Children.Remove(_pop);
        if (_dirty) { _dirty = false; Render(); }
    }

    // ---------- actions ----------
    void Launch()
    {
        try
        {
            if (!Game.Found) throw new InvalidOperationException("Schedule I not found. Run Set up again.");
            if (Game.Running != null) throw new InvalidOperationException("Close Schedule I first, then launch from here.");
            Store.Push();
            Game.Start();
            var tv = Store.Catalog.First(c => c.File == "TVPlus.dll");
            if (Store.On(tv) && !Deps.S1ApiPresent) Say("Launched, but TV+ needs S1API and it is missing. Run Set up Schedule Loader again to repair it.", Ui.Amber);
            else Say("Launched with your enabled mods.", Ui.Accent);
        }
        catch (Exception ex) { Say(ex.Message, Ui.Red); }
    }

    async void Install(ModInfo m)
    {
        try { await Setup.BuildAsync(m, s => Say(s, Ui.Amber)); Say("Installed " + m.Name, Ui.Accent); Render(); }
        catch (Exception ex)
        {
            Logger.Write("Install failed: " + ex);
            Say("Install failed.", Ui.Red);
            if (MessageBox.Show(ex.Message + "\n\nThe full details are in:\n" + Logger.LogPath + "\n\nOpen the log now?", "Schedule Loader", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes) Logger.Open();
        }
    }

    void Say(string s, Brush c) { _status.Text = s; _status.Foreground = c; }

    Border Close(Brush color)
    {
        var x = Ui.Btn("✕", Brushes.Transparent, color, Application.Current.Shutdown);
        x.HorizontalAlignment = HorizontalAlignment.Right; x.VerticalAlignment = VerticalAlignment.Top; x.Margin = new Thickness(0, 8, 10, 0);
        return x;
    }
}
