using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using S1API.TVApp;
using UnityEngine;
using UnityEngine.UI;
using TVInterface = Il2CppScheduleOne.TV.TVInterface;

namespace TVPlus;

/// <summary>App icons drawn in code: a rounded square with a white shape on it.</summary>
static class Icons
{
    // shapes, with x and y from 0 to 1
    public static bool Play(float x, float y) => x >= 0.38f && x <= 0.72f && Mathf.Abs(y - 0.5f) <= (0.72f - x) * 0.6f;

    public static bool Gear(float x, float y)
    {
        float dx = x - 0.5f, dy = y - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r < 0.1f || r > 0.33f) return false; // hole in the middle, nothing outside the teeth
        if (r <= 0.24f) return true;              // the ring
        return (Mathf.Atan2(dy, dx) / (2 * Mathf.PI) + 1f) * 8f % 1f < 0.5f; // 8 teeth
    }

    public static Sprite Make(Color bg, Func<float, float, bool> white)
    {
        const int n = 128;
        const float round = 22;
        Texture2D tex;
        try
        {
            tex = new Texture2D(n, n);
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    float cx = Mathf.Clamp(x, round, n - 1 - round), cy = Mathf.Clamp(y, round, n - 1 - round);
                    bool outside = (x - cx) * (x - cx) + (y - cy) * (y - cy) > round * round; // rounded corners
                    tex.SetPixel(x, y, outside ? Color.clear : white((x + 0.5f) / n, (y + 0.5f) / n) ? Color.white : bg);
                }
            tex.Apply();
        }
        catch (Exception e)
        {
            MelonLogger.Warning("TV+ icon fallback: " + e.Message);
            tex = new Texture2D(4, 4);
            tex.Apply();
        }
        // The icon is made at the main menu. Without this Unity frees it when the game world loads, and the TV shows a broken icon.
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }
}

/// <summary>The TV+ idle screen settings (clock font and wallpaper), saved in UserData\MelonPreferences.cfg.</summary>
static class TvLook
{
    public static readonly string[] WallNames = { "Night", "Sunset", "Forest", "Your image" };
    const int CustomWall = 3;
    static readonly Color[] Top = { new(0.16f, 0.12f, 0.38f), new(1f, 0.55f, 0.25f), new(0.3f, 0.62f, 0.3f) };
    static readonly Color[] Bottom = { new(0.02f, 0.02f, 0.08f), new(0.42f, 0.08f, 0.32f), new(0.02f, 0.12f, 0.05f) };

    static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "TVPlus");
    static string CustomFile => Path.Combine(Folder, "wallpaper.img"); // the picked file's bytes, PNG or JPG

    /// <summary>Goes up every time a setting changes, so screens know to redraw.</summary>
    public static int Version { get; private set; }

    // the game's own clock fonts, read from the first TV before TV+ changes them
    public static TMP_FontAsset GameClockFont, GameDayFont;

    static MelonPreferences_Category _cat;
    static MelonPreferences_Entry<string> _font;
    static MelonPreferences_Entry<int> _wall;
    static readonly Texture2D[] _walls = new Texture2D[3];
    static Texture2D _custom;

    static void Init()
    {
        if (_cat != null) return;
        _cat = MelonPreferences.CreateCategory("TVPlus", "TV+");
        _font = _cat.CreateEntry("ClockFont", "", "Clock font", "Name of the game font used for the TV clock. Empty = the game's own clock font.");
        _wall = _cat.CreateEntry("Wallpaper", 0, "Wallpaper", "0 Night, 1 Sunset, 2 Forest, 3 your own image.");
    }

    static void Save() { MelonPreferences.Save(); Version++; }

    public static string FontName { get { Init(); return _font.Value ?? ""; } set { Init(); _font.Value = value; Save(); } }

    public static int Wallpaper
    {
        get { Init(); int w = _wall.Value; return w == CustomWall && Custom() == null || w < 0 || w > CustomWall ? 0 : w; }
        set { Init(); _wall.Value = value; Save(); }
    }

    public static bool HasCustom => Custom() != null;

    // ---------- fonts: the TextMeshPro fonts the game has loaded ----------
    public static List<TMP_FontAsset> Fonts() =>
        Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null).GroupBy(f => f.name).Select(g => g.First()).OrderBy(f => f.name).ToList();

    public static TMP_FontAsset Font(TMP_FontAsset gameClockFont)
    {
        var name = FontName;
        return name.Length == 0 ? gameClockFont : Fonts().FirstOrDefault(f => f.name == name) ?? gameClockFont;
    }

    public static string FontLabel => FontName.Length == 0 ? "Game default" : FontName.Replace(" SDF", "");

    // ---------- wallpapers ----------
    public static Texture Wall(int i)
    {
        if (i == CustomWall) return Custom() ?? Wall(0);
        return _walls[i] ??= Gradient(Top[i], Bottom[i]);
    }

    // a vertical blend with a soft glow in the middle
    static Texture2D Gradient(Color top, Color bottom)
    {
        const int w = 128, h = 72;
        var t = new Texture2D(w, h) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float dx = (x - w / 2f) / w, dy = (y - h / 2f) / h;
                float glow = 1.15f - 0.6f * Mathf.Sqrt(dx * dx + dy * dy);
                var c = Color.Lerp(bottom, top, y / (h - 1f)) * glow;
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
        t.Apply();
        return t;
    }

    static Texture2D Custom()
    {
        if (_custom == null && File.Exists(CustomFile))
            try { _custom = LoadImage(File.ReadAllBytes(CustomFile)); } catch (Exception e) { MelonLogger.Warning("TV+ wallpaper: " + e.Message); }
        return _custom;
    }

    static Texture2D LoadImage(byte[] bytes)
    {
        var t = new Texture2D(2, 2);
        if (!ImageConversion.LoadImage(t, bytes)) { UnityEngine.Object.Destroy(t); return null; }
        t.wrapMode = TextureWrapMode.Clamp;
        t.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return t;
    }

    /// <summary>Copies the picked image into UserData\TVPlus and uses it. Returns a message for the user.</summary>
    public static string SetCustom(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return "Please pick a PNG or JPG image.";
        if (!File.Exists(path)) return "That file is gone.";
        if (new FileInfo(path).Length > 40_000_000) return "That image is too big (over 40 MB).";
        var bytes = File.ReadAllBytes(path);
        var tex = LoadImage(bytes);
        if (tex == null) return "That image could not be read.";
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(CustomFile, bytes); // a copy, so the wallpaper stays even if the original is moved
        if (_custom != null) UnityEngine.Object.Destroy(_custom);
        _custom = tex;
        Wallpaper = CustomWall;
        return "Wallpaper set.";
    }

    // fills the area like a phone wallpaper: no stretching, the overflow is cropped evenly
    internal static void Fill(RawImage img, Texture t, Vector2 area)
    {
        img.texture = t;
        float ta = (float)t.width / t.height, aa = area.x / area.y;
        img.uvRect = ta > aa ? new Rect((1 - aa / ta) / 2, 0, aa / ta, 1) : new Rect(0, (1 - ta / aa) / 2, 1, ta / aa);
    }

    /// <summary>The TV+ idle screen: wallpaper, a soft band, and the game's time and day in the chosen font.</summary>
    internal class Screen
    {
        public readonly GameObject Root;
        readonly RawImage _wall;
        readonly TextMeshProUGUI _time, _day;
        readonly Vector2 _size;
        readonly TVInterface _tv;
        int _ver = -1;

        public Screen(Transform parent, Vector2 size, TVInterface tv)
        {
            _size = size;
            _tv = tv;
            Root = VideosApp.Make("Idle", parent, size, Vector2.zero);
            _wall = VideosApp.Make("Wallpaper", Root.transform, size, Vector2.zero).AddComponent<RawImage>();
            _wall.raycastTarget = false;
            var band = VideosApp.Make("Band", Root.transform, new Vector2(size.x * 0.6f, size.y * 0.46f), Vector2.zero).AddComponent<Image>();
            band.color = new Color(0f, 0f, 0f, 0.35f); // keeps the clock readable on bright wallpapers
            band.raycastTarget = false;
            _time = Text("Time", size.y * 0.2f, new Vector2(size.x, size.y * 0.26f), new Vector2(0, size.y * 0.06f));
            _day = Text("Day", size.y * 0.08f, new Vector2(size.x, size.y * 0.12f), new Vector2(0, -size.y * 0.11f));
            Tick();
        }

        TextMeshProUGUI Text(string name, float fontSize, Vector2 box, Vector2 pos)
        {
            var t = VideosApp.Make(name, Root.transform, box, pos).AddComponent<TextMeshProUGUI>();
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        public void Tick()
        {
            var timeLabel = _tv != null ? _tv.TimeLabel : null;
            if (_ver != Version)
            {
                _ver = Version;
                Fill(_wall, Wall(Wallpaper), _size);
                var f = Font(GameClockFont ?? (timeLabel != null ? timeLabel.font : null));
                if (f != null) { _time.font = f; _day.font = f; }
            }
            // copied from the game's own clock, so the time and day always match the game
            if (timeLabel != null) _time.text = timeLabel.text;
            if (_tv != null && _tv.Daylabel != null) _day.text = _tv.Daylabel.text;
        }
    }
}

/// <summary>The "Settings" app: clock font and wallpaper for the TV+ idle screen.</summary>
public class SettingsApp : TVApp
{
    static readonly List<SettingsApp> All = new();
    const float RefW = 960, RefH = 540;
    static readonly Color Dark = new(0.2f, 0.28f, 0.22f);
    static Sprite _icon;

    protected override string AppName => "TVPlusSettings";
    protected override string AppTitle => "Settings";
    protected override Sprite Icon => _icon ??= Icons.Make(new Color(0.24f, 0.28f, 0.34f), Icons.Gear);

    TVInterface _tv;
    TvLook.Screen _preview;
    Text _fontName, _status;
    readonly Image[] _wallBtns = new Image[4];
    volatile string _picked; // set by the file dialog thread ("" = cancelled)
    volatile bool _picking;
    int _ver = -1;
    CursorLockMode _prevLock;
    bool _prevVisible;

    protected override void OnCreatedUI(GameObject container)
    {
        if (!All.Contains(this)) All.Add(this); // S1API builds the app again whenever a TV is placed
        for (var t = container.transform; t != null && _tv == null; t = t.parent) _tv = t.GetComponent<TVInterface>();
        var area = VideosApp.ScreenArea(container);
        var avail = area != null ? area.rect.size : Vector2.zero;
        float scale = avail.x > 0 ? Mathf.Min(avail.x / RefW, avail.y / RefH) : 1f;

        try
        {
            if (avail.x > 0) VideosApp.Make("Back", container.transform, avail, Vector2.zero).AddComponent<RawImage>().color = Color.black;
            var root = VideosApp.Make("TVPlusSettingsRoot", container.transform, new Vector2(RefW, RefH), Vector2.zero);
            root.transform.localScale = new Vector3(scale, scale, 1f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.07f, 0.05f);
            bg.raycastTarget = false;
            var r = root.transform;

            VideosApp.Label("Title", "TV+ Settings", r, 34, FontStyle.Bold, VideosApp.Cream, new Vector2(RefW - 40, 50), new Vector2(0, 240));

            // left: a live preview of the idle screen
            VideosApp.Label("PreviewTitle", "Your TV when nobody is using it", r, 20, FontStyle.Italic, new Color(0.6f, 0.65f, 0.6f), new Vector2(420, 30), new Vector2(-230, 195));
            _preview = new TvLook.Screen(VideosApp.Make("Preview", r, new Vector2(400, 225), new Vector2(-230, 60)).transform, new Vector2(400, 225), _tv);

            // right: the controls, all in the upper part of the screen (clicks low on the TV screen do not get through)
            VideosApp.Label("FontTitle", "Clock font", r, 24, FontStyle.Bold, VideosApp.Cream, new Vector2(390, 30), new Vector2(230, 195));
            VideosApp.Btn("<", r, new Vector2(60, 50), new Vector2(65, 150), Dark, Color.white, () => CycleFont(-1));
            _fontName = VideosApp.Label("FontName", "", r, 22, FontStyle.Normal, Color.white, new Vector2(250, 50), new Vector2(230, 150));
            VideosApp.Btn(">", r, new Vector2(60, 50), new Vector2(395, 150), Dark, Color.white, () => CycleFont(1));

            VideosApp.Label("WallTitle", "Wallpaper", r, 24, FontStyle.Bold, VideosApp.Cream, new Vector2(390, 30), new Vector2(230, 100));
            for (int i = 0; i < 3; i++)
            {
                int w = i;
                _wallBtns[i] = VideosApp.Btn(TvLook.WallNames[i], r, new Vector2(125, 50), new Vector2(100 + i * 130, 55), Dark, Color.white, () => TvLook.Wallpaper = w);
            }
            _wallBtns[3] = VideosApp.Btn(TvLook.WallNames[3], r, new Vector2(190, 50), new Vector2(132, -5), Dark, Color.white, UseCustom);
            VideosApp.Btn("Upload image…", r, new Vector2(190, 50), new Vector2(328, -5), VideosApp.Green, VideosApp.Ink, Upload);

            _status = VideosApp.Label("Status", "", r, 20, FontStyle.Normal, Color.white, new Vector2(400, 40), new Vector2(230, -60));
        }
        catch (Exception e) { MelonLogger.Error("TV+ settings layout failed: " + e); }
    }

    protected override void OnOpened()
    {
        _prevLock = Cursor.lockState;
        _prevVisible = Cursor.visible;
        VideosApp.CursorFree = true;
        _ver = -1; // redraw labels
        Say("");
    }

    protected override void OnClosed()
    {
        VideosApp.CursorFree = false;
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;
    }

    internal static void TickAll()
    {
        foreach (var app in All.ToArray())
            try { app.Tick(); }
            catch { All.Remove(app); } // its TV is gone (scene change)
    }

    void Tick()
    {
        if (_picked != null)
        {
            var f = _picked;
            _picked = null;
            try { Say(f.Length > 0 ? TvLook.SetCustom(f) : ""); }
            catch (Exception e) { MelonLogger.Warning("TV+ wallpaper: " + e.Message); Say("That image could not be used."); }
        }
        if (!IsOpen) return;
        if (_ver != TvLook.Version)
        {
            _ver = TvLook.Version;
            if (_fontName != null) _fontName.text = TvLook.FontLabel;
            for (int i = 0; i < _wallBtns.Length; i++)
                if (_wallBtns[i] != null) _wallBtns[i].color = i == TvLook.Wallpaper ? VideosApp.Green : Dark;
        }
        _preview?.Tick();
    }

    // steps through "Game default" and then every font the game has loaded
    void CycleFont(int dir)
    {
        var fonts = TvLook.Fonts();
        int i = fonts.FindIndex(f => f.name == TvLook.FontName); // -1 = game default (or a font that is no longer loaded)
        int n = fonts.Count + 1;
        i = ((i + 1 + dir) % n + n) % n - 1;
        TvLook.FontName = i < 0 ? "" : fonts[i].name;
    }

    void UseCustom()
    {
        if (TvLook.HasCustom) TvLook.Wallpaper = 3;
        else Say("Press Upload image… to pick one first.");
    }

    void Upload()
    {
        if (_picking) return;
        _picking = true;
        FilePicker.Pick("Images (*.png, *.jpg)\0*.png;*.jpg;*.jpeg\0", "Choose a wallpaper for the TV", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), f => { _picked = f; _picking = false; });
        Say("Pick an image in the window that opened.");
    }

    void Say(string text) { if (_status != null) _status.text = text; }
}
