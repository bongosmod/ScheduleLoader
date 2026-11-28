using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;
using S1API.TVApp;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(TVPlus.Core), "TV+", "0.3.0", "Bongosmod")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace TVPlus;

public class Core : MelonMod
{
    public override void OnInitializeMelon()
    {
        // This line in MelonLoader\Latest.log tells you whether the TV app framework is there.
        var s1 = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Where(n => n.Contains("S1API")).ToList();
        if (s1.Count > 0) LoggerInstance.Msg("TV+ 0.3.0 loaded. S1API found: " + string.Join(", ", s1));
        else LoggerInstance.Warning("TV+ loaded, but S1API was not found, so the Videos app cannot appear. Run Set up in Schedule Loader to repair it.");
    }

    public override void OnLateUpdate()
    {
        VideosApp.TickAll(); // runs while the apps are closed too, so the TV screen keeps going
        SettingsApp.TickAll();

        // The game keeps locking the mouse, so while the Videos app is open we free it again after the game has had its turn.
        if (!VideosApp.CursorFree) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

#if HAS_VIDEO
/// <summary>Thin wrapper so the rest of the app builds even when this game has no video module.</summary>
class Player
{
    readonly UnityEngine.Video.VideoPlayer _p;
    public Player(GameObject host, AudioSource audio, RenderTexture tex)
    {
        _p = host.AddComponent<UnityEngine.Video.VideoPlayer>();
        _p.playOnAwake = false;
        _p.source = UnityEngine.Video.VideoSource.Url;
        _p.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
        _p.targetTexture = tex;
        _p.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.AudioSource;
        _p.SetTargetAudioSource(0, audio);
    }
    public bool Prepared => _p.isPrepared;
    public bool Ended => _p.length > 0 && _p.time >= _p.length - 0.15; // isPlaying is not trustworthy: it said true before any video was loaded
    public bool IsPaused => _p.isPaused;
    public void Load(string url) { _p.url = url; _p.Prepare(); }
    public void Play() => _p.Play();
    public void Pause() => _p.Pause();
    public void Stop() => _p.Stop();
    public double Time { get => _p.time; set => _p.time = value; } // playback position in seconds
}
#else
class Player
{
    public Player(GameObject host, AudioSource audio, RenderTexture tex) { throw new NotSupportedException("This game build has no video module."); }
    public bool Prepared => false;
    public bool Ended => true;
    public bool IsPaused => false;
    public void Load(string url) { }
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public double Time { get => 0; set { } }
}
#endif

/// <summary>Windows "Open file" dialog.</summary>
static class FilePicker
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Ofn
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir, lpstrTitle;
        public int Flags;
        public short nFileOffset, nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData, lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetOpenFileNameW(ref Ofn ofn);

    /// <summary>Opens the dialog on its own thread and calls done there with the picked path, or "" if cancelled.</summary>
    public static void Pick(string filter, string title, string startDir, Action<string> done)
    {
        var owner = Process.GetCurrentProcess().MainWindowHandle; // keeps the dialog in front of the game
        var t = new Thread(() =>
        {
            try { done(Open(owner, filter, title, startDir) ?? ""); }
            catch (Exception e) { MelonLogger.Warning("TV+ file picker: " + e.Message); done(""); }
        });
        t.SetApartmentState(ApartmentState.STA); // the Windows file dialog needs an STA thread
        t.IsBackground = true;
        t.Start();
    }

    /// <summary>Blocks until the user picks a file (its full path) or cancels (null). Call it on an STA thread.</summary>
    static string Open(IntPtr owner, string filter, string title, string startDir)
    {
        const int max = 1024;
        var buf = Marshal.AllocHGlobal(max * 2);
        try
        {
            Marshal.WriteInt16(buf, 0);
            var o = new Ofn
            {
                lStructSize = Marshal.SizeOf<Ofn>(),
                hwndOwner = owner,
                lpstrFilter = filter, // "Name\0*.ext\0": the marshaller adds the final \0
                lpstrFile = buf,
                nMaxFile = max,
                lpstrTitle = title,
                lpstrInitialDir = startDir,
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008, // explorer look, file and folder must exist, keep the game's working folder
            };
            return GetOpenFileNameW(ref o) ? Marshal.PtrToStringUni(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}

/// <summary>The "Videos" app. S1API finds this class by itself and adds it to the TV home screen.</summary>
public class VideosApp : TVApp
{
    internal static bool CursorFree;
    static readonly List<VideosApp> All = new();

    // The screen is designed once at 960 x 540 (16:9) and then scaled to whatever size this TV's screen area really is.
    const float RefW = 960, RefH = 540;
    internal static readonly Color Cream = new(0.95f, 0.93f, 0.78f), Green = new(0.49f, 0.86f, 0.33f), Ink = new(0.03f, 0.08f, 0.04f);
    const string NoVideo = "Video playback is not available in this game build.";
    static Sprite _icon;

    static VideosApp() { MelonLogger.Msg("TV+: S1API found the Videos app."); } // proves S1API discovered it

    protected override string AppName => "TVPlusVideos";
    protected override string AppTitle => "Videos";
    protected override Sprite Icon => _icon ??= Icons.Make(new Color(0.16f, 0.62f, 0.26f), Icons.Play);

    Text _status, _fileName;
    RawImage _screen;
    RenderTexture _tex;
    GameObject _overlay, _host, _video; // our layer on the TV while the app is closed, the player's object, the video in that layer
    TvLook.Screen _idle; // the TV+ idle screen (wallpaper + clock) in that layer
    int _lookVer = -1;
    float _lift; // how far out of the screen our layer sits, measured from the game's clock
    string _shown; // what the layer showed last frame, so changes are logged once
    RectTransform _area; // the TV's screen area, the part of the TV the Videos app fills
    Il2CppScheduleOne.TV.TVInterface _tv;
    Player _player;
    volatile string _picked; // set by the file dialog thread ("" = cancelled), read on the main thread
    volatile bool _picking;
    bool _preparing;
    float _deadline;
    CursorLockMode _prevLock;
    bool _prevVisible;

    // ---------- layout (each part is guarded, so a problem in one never stops the app from appearing) ----------
    protected override void OnCreatedUI(GameObject container)
    {
        Cleanup(); // S1API builds the app again whenever a TV is placed: drop the old screen first, or it stays behind
        if (!All.Contains(this)) All.Add(this);
        _area = ScreenArea(container);
        var avail = _area != null ? _area.rect.size : Vector2.zero;
        float scale = avail.x > 0 ? Mathf.Min(avail.x / RefW, avail.y / RefH) : 1f;
        MelonLogger.Msg($"TV+: screen area {avail.x:0}x{avail.y:0}, scale {scale:0.00}");

        try
        {
            if (avail.x > 0) Make("Back", container.transform, avail, Vector2.zero).AddComponent<RawImage>().color = Color.black; // fills any leftover edges

            var root = Make("TVPlusRoot", container.transform, new Vector2(RefW, RefH), Vector2.zero);
            root.transform.localScale = new Vector3(scale, scale, 1f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.07f, 0.05f);
            bg.raycastTarget = false;

            Label("Title", "Play a video from your PC", root.transform, 36, FontStyle.Bold, Cream, new Vector2(RefW - 40, 50), new Vector2(0, 232));
            // all buttons sit in the top row: clicks near the bottom of the TV screen did not get through
            var dark = new Color(0.2f, 0.28f, 0.22f);
            Btn("Choose MP4…", root.transform, new Vector2(200, 60), new Vector2(-330, 170), Green, Ink, ChooseClicked);
            Btn("Group watch", root.transform, new Vector2(200, 60), new Vector2(-115, 170), Green, Ink, GroupClicked);
            Btn("Pause / Resume", root.transform, new Vector2(210, 60), new Vector2(105, 170), dark, Color.white, TogglePause);
            Btn("Stop", root.transform, new Vector2(140, 60), new Vector2(295, 170), dark, Color.white, Stop);
            _fileName = Label("FileName", "", root.transform, 22, FontStyle.Italic, new Color(0.55f, 0.6f, 0.55f), new Vector2(RefW - 40, 30), new Vector2(0, 118));

            // the picture
            _screen = Make("Screen", root.transform, new Vector2(640, 360), new Vector2(0, -80)).AddComponent<RawImage>();
            _screen.color = Color.black;
            _screen.raycastTarget = false;
            _status = Label("Status", "", root.transform, 26, FontStyle.Bold, Color.white, new Vector2(600, 80), new Vector2(0, -80));
        }
        catch (Exception e) { MelonLogger.Error("TV+ screen layout failed: " + e); return; }

        for (var t = container.transform; t != null && _tv == null; t = t.parent) _tv = t.GetComponent<Il2CppScheduleOne.TV.TVInterface>();
        _hasTv = _tv != null;
        if (_hasTv) HideSnake();

        try
        {
            _tex = new RenderTexture(1280, 720, 0);
            _screen.texture = _tex;
            _screen.color = Color.white;

            // The game switches its TV objects on and off as you use the TV and walk away, which stops or hides anything
            // inside them. So the player and the TV picture are our own top-level objects, kept lined up with the TV each frame.
            _host = new GameObject("TVPlusPlayer");
            // the sound comes out of the TV itself
            var audio = _host.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.spatialBlend = 1f; audio.minDistance = 2f; audio.maxDistance = 20f;
            _player = new Player(_host, audio, _tex);
            MelonLogger.Msg("TV+: video player ready.");
        }
        catch (Exception e)
        {
            _player = null;
            MelonLogger.Error("TV+ video player could not start: " + e);
            Say(NoVideo);
        }

        // the TV screen is separate, so a problem there never takes the Videos app down with it
        if (_area == null) { MelonLogger.Warning("TV+: TV screen size unknown, so nothing shows on the TV outside the app."); return; }
        try { MakeOverlay(_tex); }
        catch (Exception e)
        {
            MelonLogger.Error("TV+ TV screen failed: " + e);
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
        }
    }

    // One world-space layer of our own on the TV's screen area, lined up with it every frame (the way the video already
    // showed up on the TV). It holds the TV+ idle screen (wallpaper, time and day in the chosen font, copied from the
    // game's clock every frame) and, on top, the video while one plays. It covers the game's own day/time screen.
    void MakeOverlay(Texture tex)
    {
        int layer = _area.gameObject.layer; // same layer as the TV's own screen, so the game camera draws it

        // the game's own clock fonts, so "Game default" can always go back to them
        if (_tv != null && _tv.TimeLabel != null && TvLook.GameClockFont == null) TvLook.GameClockFont = _tv.TimeLabel.font;
        if (_tv != null && _tv.Daylabel != null && TvLook.GameDayFont == null) TvLook.GameDayFont = _tv.Daylabel.font;

        _overlay = new GameObject("TVPlusScreen");
        var canvas = _overlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200; // over the game's own idle screen
        _overlay.GetComponent<RectTransform>().sizeDelta = _area.rect.size;

        _idle = new TvLook.Screen(_overlay.transform, _area.rect.size, _tv);

        _video = Make("Video", _overlay.transform, _area.rect.size, Vector2.zero); // made after the idle screen, so it is on top
        var img = _video.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;

        foreach (var t in _overlay.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        FollowTv();
        MelonLogger.Msg($"TV+: TV screen made, {_area.rect.width:0}x{_area.rect.height:0}, clock {(_tv != null && _tv.TimeLabel != null ? "found" : "not found")}");
        _overlay.SetActive(false);
    }

    // Puts our layer (and the sound) exactly on the TV screen area, even if the TV was moved.
    void FollowTv()
    {
        var r = _area.rect;
        var bl = _area.TransformPoint(new Vector3(r.xMin, r.yMin, 0));
        var tl = _area.TransformPoint(new Vector3(r.xMin, r.yMax, 0));
        var br = _area.TransformPoint(new Vector3(r.xMax, r.yMin, 0));
        Vector3 right = br - bl, up = tl - bl, center = (br + tl) / 2;
        _host.transform.position = center;
        if (_overlay == null) return;

        var rt = _overlay.GetComponent<RectTransform>();
        rt.sizeDelta = r.size;
        rt.rotation = Quaternion.LookRotation(Vector3.Cross(right, up), up);
        // Mirrored left to right: seen from the room, the layer came out back to front. Mirroring the whole layer
        // fixes the video and the clock text together.
        rt.localScale = new Vector3(-right.magnitude / r.width, up.magnitude / r.height, 1f);

        // out of the screen, on the side the player sees, and in front of the game's clock (the game's black idle
        // screen sits behind its clock, so anything in front of the clock is in front of that too)
        var normal = Vector3.Cross(right, up).normalized;
        var cam = Camera.main;
        var outward = cam != null && Vector3.Dot(normal, cam.transform.position - center) < 0 ? -normal : normal;
        float clockLift = 0f;
        if (_tv != null)
        {
            if (_tv.TimeLabel != null) clockLift = Mathf.Max(clockLift, Vector3.Dot(_tv.TimeLabel.transform.position - center, outward));
            if (_tv.Daylabel != null) clockLift = Mathf.Max(clockLift, Vector3.Dot(_tv.Daylabel.transform.position - center, outward));
        }
        _lift = clockLift + 0.004f;
        rt.position = center + outward * _lift;
    }

    // The game's Snake app: hide its home screen button and the game itself. Its button is the one showing Snake's icon.
    void HideSnake()
    {
        try
        {
            var snake = _tv.GetComponentInChildren<Il2CppScheduleOne.TV.Snake>(true);
            if (snake == null) return;
            var home = _tv.HomeScreen;
            var icon = snake.Icon;
            bool found = false;
            if (home != null && home.AppButtonContainer != null && icon != null)
                foreach (var img in home.AppButtonContainer.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null || img.sprite.Pointer != icon.Pointer) continue;
                    var button = img.transform; // climb to the direct child of the container: that is the whole button
                    while (button.parent != null && button.parent != home.AppButtonContainer) button = button.parent;
                    button.gameObject.SetActive(false);
                    found = true;
                }
            if (!found) { MelonLogger.Warning("TV+: Snake button not found, leaving Snake alone."); return; }
            if (snake.IsOpen) snake.Close();
            snake.gameObject.SetActive(false);
        }
        catch (Exception e) { MelonLogger.Warning("TV+: could not hide Snake: " + e.Message); }
    }

    // size of the TV screen area: the first container (or parent) that has a real size
    internal static RectTransform ScreenArea(GameObject c)
    {
        for (var t = c.transform; t != null; t = t.parent)
        {
            var rt = t.GetComponent<RectTransform>();
            if (rt != null && rt.rect.width >= 200 && rt.rect.height >= 100) return rt;
        }
        return null;
    }

    internal static GameObject Make(string name, Transform parent, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Place(go, size, pos);
        return go;
    }

    internal static void Place(GameObject go, Vector2 size, Vector2 pos)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    internal static Text Label(string name, string text, Transform parent, int size, FontStyle style, Color color, Vector2 box, Vector2 pos)
    {
        var t = UIFactory.Text(name, text, parent, size, TextAnchor.MiddleCenter, style);
        t.color = color;
        t.raycastTarget = false; // text must never block clicks
        Place(t.gameObject, box, pos);
        return t;
    }

    internal static Image Btn(string label, Transform parent, Vector2 size, Vector2 pos, Color fill, Color textColor, Action click)
    {
        var go = Make(label, parent, size, pos);
        var img = go.AddComponent<Image>();
        img.color = fill;
        var b = go.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        b.onClick.AddListener((UnityAction)new Action(click));
        Label("Label", label, go.transform, 24, FontStyle.Bold, textColor, size, Vector2.zero);
        return img;
    }

    // ---------- app lifecycle ----------
    protected override void OnOpened()
    {
        _prevLock = Cursor.lockState;
        _prevVisible = Cursor.visible;
        CursorFree = true; // free mouse while the app is open
        if (_player == null) Say(NoVideo);
        else if (!Active) Say("Press Choose MP4 to pick a video from your PC.");
    }

    // The video is not stopped here: it keeps playing on the TV screen until Stop is pressed.
    protected override void OnClosed()
    {
        CursorFree = false;
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;
    }

    internal static void TickAll()
    {
        GroupWatch.Poll();
        // shortcut: a group watch message goes to the first TV only, upgrade to "the TV you last used" if people ask for it
        while (GroupWatch.TryTake(out var msg))
            try { All.FirstOrDefault()?.OnGroupMessage(msg); }
            catch (Exception e) { MelonLogger.Warning("TV+ group watch: " + e.Message); }

        foreach (var app in All.ToArray())
            try { app.Tick(); }
            catch (Exception e)
            {
                MelonLogger.Warning("TV+: TV screen stopped: " + e);
                app.Cleanup();
                All.Remove(app);
            }
    }

    // Deletes our own TV layers and player. They are top-level objects, so nothing else removes them when the TV goes.
    void Cleanup()
    {
        foreach (var o in new UnityEngine.Object[] { _overlay, _host, _tex })
            try { if (o != null) UnityEngine.Object.Destroy(o); } catch { }
        _overlay = _host = _video = null;
        _idle = null;
        _lookVer = -1;
        _shown = null;
        _tex = null;
        _player = null;
        _tv = null;
        _hasTv = false;
        _area = null;
        _preparing = _paused = _playing = false;
    }

    bool _hasTv;

    void Tick()
    {
        // the TV was picked up, moved or sold: Unity reports its destroyed objects as null
        if (_hasTv && (_tv == null || _area == null))
        {
            MelonLogger.Msg($"TV+: TV gone ({(_tv == null ? "TV" : "screen area")} removed), taking its screen down.");
            Cleanup();
            All.Remove(this);
            return;
        }

        if (_picked != null)
        {
            var f = _picked;
            _picked = null;
            if (f.Length == 0) Say("");
            else if (_pickGroup) StartGroup(f);
            else { LeaveGroup(); Begin(f); }
        }

        if (_preparing && _player != null)
        {
            if (_player.Prepared)
            {
                _preparing = false;
                _playing = true;
                _player.Play();
                if (_startAt > 0) _player.Time = _startAt + (Time.unscaledTime - _beginStamp); // catch up with the group
                Say("");
            }
            else if (Time.unscaledTime > _deadline) { _preparing = false; _player.Stop(); Say("This video could not be played. MP4 files with H.264 video work best."); }
        }
        if (_playing && !_paused && _player != null && _player.Ended) { LeaveGroup(); StopLocal(); } // video finished: back to the idle screen

        // the one who started a group watch tells everyone where the video is now and then: fixes drift, and lets late joiners in
        if (_groupHost && _groupFile != null && _playing && !_paused && Time.unscaledTime >= _nextSync)
        {
            _nextSync = Time.unscaledTime + 10f;
            GroupWatch.Send("sync", _player.Time, _groupFile);
        }

        if (_overlay != null)
        {
            // Shown whenever nobody is using the TV (where the game shows its day/time screen); hidden while someone is at
            // the TV, so the home screen and its apps stay usable. Shows the video if one is playing, otherwise the idle screen.
            bool show = !IsOpen && (_tv == null || !_tv.IsOpen);
            bool video = Active;
            if (_overlay.activeSelf != show) _overlay.SetActive(show);
            if (show)
            {
                FollowTv();
                if (_video.activeSelf != video) _video.SetActive(video);
                if (_idle.Root.activeSelf == video) _idle.Root.SetActive(!video);
                if (!video) _idle.Tick();
            }
            var now = !show ? (IsOpen ? "off (Videos app open)" : "off (someone is using the TV)") : video ? "video" : "idle screen";
            if (now != _shown)
            {
                _shown = now;
                MelonLogger.Msg($"TV+: TV screen: {now}" + (show ? $", {TvLook.WallNames[TvLook.Wallpaper]} wallpaper, {_lift * 1000:0.0} mm out, at {_overlay.transform.position}" : ""));
            }
        }
        ApplyLook();
    }

    // Settings changed: new wallpaper, and the chosen font on the game's own clock (time and day keep updating as normal).
    void ApplyLook()
    {
        if (_lookVer == TvLook.Version) return;
        _lookVer = TvLook.Version;
        if (_tv == null) return; // (the idle screen redraws itself when settings change)
        var timeFont = TvLook.Font(TvLook.GameClockFont);
        if (timeFont != null && _tv.TimeLabel != null) _tv.TimeLabel.font = timeFont;
        var dayFont = TvLook.Font(TvLook.GameDayFont);
        if (dayFont != null && _tv.Daylabel != null) _tv.Daylabel.font = dayFont;
    }

    // ---------- playing ----------
    void ChooseClicked()
    {
        if (_player == null) { Say(NoVideo); return; }
        if (_picking) return;
        _picking = true;
        _pickGroup = false;
        FilePicker.Pick("MP4 video (*.mp4)\0*.mp4\0", "Choose a video for the TV", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), f => { _picked = f; _picking = false; });
        Say("Pick an MP4 in the window that opened.");
    }

    // startAt: where to start, in seconds (group watch joins a video already playing)
    bool Begin(string path, float startAt = 0)
    {
        if (_player == null) return false;
        if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { Say("Please pick an .mp4 file."); return false; }
        StopLocal();
        _player.Load(path);
        _preparing = true;
        _startAt = startAt;
        _beginStamp = Time.unscaledTime;
        _deadline = Time.unscaledTime + 25f;
        if (_fileName != null) _fileName.text = Path.GetFileName(path);
        Say("Loading video…");
        return true;
    }

    bool _paused, _playing; // our own flags: the player's own playing and paused states are not reliable to read back
    bool Active => _player != null && (_preparing || _paused || _playing);

    void TogglePause()
    {
        if (_player == null || !Active || _preparing) return;
        if (_paused) _player.Play(); else _player.Pause();
        _paused = !_paused;
        Say(_paused ? "Paused" : "");
        if (_groupFile != null) GroupWatch.Send(_paused ? "pause" : "resume", _player.Time, _groupFile);
    }

    // Stop button: in a group watch it stops it for everyone.
    void Stop()
    {
        if (_groupFile != null) GroupWatch.Send("stop", 0, _groupFile);
        LeaveGroup();
        StopLocal();
    }

    // ---------- group watch ----------
    string _groupFile, _missing; // the shared video's file name; a missing file we already told the player about
    bool _groupHost, _pickGroup;
    float _startAt, _beginStamp, _nextSync;

    void LeaveGroup() { _groupFile = null; _groupHost = false; }

    void GroupClicked()
    {
        if (_player == null) { Say(NoVideo); return; }
        if (_picking) return;
        Directory.CreateDirectory(GroupWatch.Folder);
        _picking = true;
        _pickGroup = true;
        FilePicker.Pick("MP4 video (*.mp4)\0*.mp4\0", "Group watch: choose the video everyone will watch", GroupWatch.Folder, f => { _picked = f; _picking = false; });
        Say("Pick the video everyone will watch.");
    }

    void StartGroup(string path)
    {
        if (!Begin(path)) return;
        _groupFile = Path.GetFileName(path);
        _groupHost = true;
        _nextSync = Time.unscaledTime + 10f;
        if (!GroupWatch.InMultiplayer)
        {
            if (_fileName != null) _fileName.text = "You are not in a multiplayer game, so this only plays for you.";
            return;
        }
        GroupWatch.Send("play", 0, _groupFile);
        if (_fileName != null) _fileName.text = $"Group watch: {_groupFile}. Friends need this file in {GroupWatch.FolderLabel}.";
    }

    // A message from another player's TV+ (already checked by GroupWatch.Parse).
    void OnGroupMessage(GroupWatch.Msg m)
    {
        if (_player == null) return;
        bool ours = _groupFile == m.File && Active;
        switch (m.Command)
        {
            case "sync" when ours:
                if (!_preparing && !_paused && Math.Abs(_player.Time - m.Time) > 2) _player.Time = m.Time; // drifted: jump back in line
                return;
            case "play":
            case "sync": // a group watch we are not in yet (we joined late, or missed the start)
                var path = Path.Combine(GroupWatch.Folder, m.File);
                if (!File.Exists(path))
                {
                    if (_missing == m.File) return; // say it once, not on every sync
                    _missing = m.File;
                    MelonLogger.Msg($"TV+: a friend started a group watch of \"{m.File}\", but it is not in {GroupWatch.Folder}");
                    if (_fileName != null) _fileName.text = $"A friend is group watching \"{m.File}\". Put that file in {GroupWatch.FolderLabel} to join.";
                    return;
                }
                if (!Begin(path, m.Time)) return;
                _groupFile = m.File;
                _groupHost = false;
                _missing = null;
                if (_fileName != null) _fileName.text = $"Group watch: {m.File}";
                return;
            case "pause" when ours && !_preparing:
                if (!_paused) { _player.Pause(); _paused = true; }
                _player.Time = m.Time;
                Say("Paused by a friend");
                return;
            case "resume" when ours && !_preparing:
                _player.Time = m.Time;
                if (_paused) { _player.Play(); _paused = false; }
                Say("");
                return;
            case "stop" when _groupFile == m.File:
                LeaveGroup();
                StopLocal();
                Say("A friend stopped the group watch.");
                return;
        }
    }

    // Stops the sound, blanks the picture and takes it off the TV (this player only).
    void StopLocal()
    {
        _preparing = false;
        _paused = _playing = false;
        if (_player != null) _player.Stop();
        if (_tex != null)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _tex;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }
        if (_fileName != null) _fileName.text = "";
        Say(_player == null ? NoVideo : "Press Choose MP4 to pick a video from your PC.");
    }

    void Say(string text) { if (_status != null) _status.text = text; }

}
