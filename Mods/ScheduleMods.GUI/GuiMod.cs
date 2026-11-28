using Il2CppTMPro;
using MelonLoader;
using ScheduleMods.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(ScheduleMods.Menu.GuiMod), "ScheduleMods GUI", "0.2.0", "Bongosmod")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ScheduleMods.Menu; // not "GUI": that would hide UnityEngine.GUI

/// <summary>
/// Adds a green "Modding" button under Quit in the main menu and the pause menu. It opens the Schedule Mods panel:
/// switched-off mods on the left, switched-on mods on the right; select one and press Enable or Disable.
/// Changes are file renames in the Mods folder, so they take effect the next time the game starts.
/// </summary>
public class GuiMod : MelonMod
{
    // ---------- look ----------
    static readonly Color Lime = new(0.72f, 0.94f, 0.18f), Red = new(0.93f, 0.27f, 0.27f);
    static readonly Color Panel = new(0.105f, 0.11f, 0.12f, 0.98f), Well = new(0.07f, 0.075f, 0.08f), CardBg = new(0.16f, 0.17f, 0.185f);
    static readonly Color TextMain = new(0.95f, 0.96f, 0.97f), TextDim = new(0.6f, 0.63f, 0.66f), Soft = new(0.24f, 0.25f, 0.27f);
    const float ListW = 500, ListH = 470, CardH = 100;

    // what we know about our own mods (also covers switched-off ones, which MelonLoader never loaded)
    static readonly Dictionary<string, (string Name, string Author, string Thumb)> Known = new()
    {
        ["ScheduleMods.Utils"] = ("ScheduleMods Utils", "Bongosmod", "utils.png"),
        ["ScheduleMods.GUI"] = ("ScheduleMods GUI", "Bongosmod", "gui.png"),
        ["BetterStackables"] = ("Better Stackables", "Bongosmod", "stackables.png"),
        ["TVPlus"] = ("TV+", "Bongosmod", "tvplus.png"),
        ["Backpacks"] = ("The Backpacks Mod", "Bongosmod", "backpacks.png"),
    };
    // thumbnails the Schedule Loader app unpacked on this PC
    static string ThumbDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLoader", "Assets");

    static TMP_FontAsset _font; // the game's own menu font, taken from its Quit button
    static Sprite _round;
    static readonly Dictionary<string, Texture2D> Thumbs = new();

    GameObject _root, _info;
    RectTransform _offList, _onList;
    TextMeshProUGUI _offTitle, _onTitle, _hint;
    string _selected; // file name (without .dll) of the selected mod
    bool _search;
    float _timer;
    int _tries;

    // ---------- the Modding button ----------
    public override void OnSceneWasInitialized(int index, string name) { _search = true; _tries = 0; }

    public override void OnUpdate()
    {
        if (_root != null && _root.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
        // the menus can appear a moment after the scene loads, so look a few times
        if (!_search) return;
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.5f) return;
        _timer = 0;
        if (AddButtons() >= 2 || ++_tries > 20) _search = false; // main menu and pause menu: up to two Quit buttons per scene
    }

    static string Label(Button b)
    {
        var t = b.GetComponentInChildren<TMP_Text>(true);
        if (t != null) return t.text;
        var u = b.GetComponentInChildren<Text>(true);
        return u != null ? u.text : null;
    }

    // Adds a Modding button under every menu Quit button, including ones in menus that are hidden right now (the pause menu).
    int AddButtons()
    {
        int done = 0;
        foreach (var quit in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (!quit.gameObject.scene.IsValid() || quit.transform.parent == null) continue; // skip prefabs and assets
            var label = Label(quit)?.Trim().ToLowerInvariant();
            if (label != "quit" && label != "quit game" && label != "quit to menu" && label != "exit" && label != "exit game") continue;
            var parent = quit.transform.parent;
            if (parent.GetComponentsInChildren<Button>(true).Length < 3) continue; // a menu, not a yes/no box
            done++;
            if (parent.Find("ModdingButton") != null) continue;

            _font ??= quit.GetComponentInChildren<TMP_Text>(true)?.font;
            var go = UnityEngine.Object.Instantiate(quit.gameObject, parent);
            go.name = "ModdingButton";
            var q = quit.GetComponent<RectTransform>();
            go.GetComponent<RectTransform>().anchoredPosition = q.anchoredPosition - new Vector2(0, q.rect.height + 10f);
            go.transform.SetSiblingIndex(quit.transform.GetSiblingIndex() + 1);

            var btn = go.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener((UnityAction)new Action(Open));
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.text = "Modding"; tmp.color = Lime; }
            var txt = go.GetComponentInChildren<Text>(true);
            if (txt != null) { txt.text = "Modding"; txt.color = Lime; }
        }
        return done;
    }

    // ---------- the panel ----------
    void Open()
    {
        try
        {
            if (_root == null) Build();
            _root.SetActive(true);
            _info.SetActive(false);
            Refresh();
        }
        catch (Exception e) { MelonLogger.Error("Schedule Mods panel failed: " + e); }
    }

    void Close() { if (_root != null) _root.SetActive(false); }

    void Build()
    {
        _root = new GameObject("ScheduleModsGUI");
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // above the game's menus
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        // dim the game behind, and catch clicks so they do not reach the menu underneath
        var dim = Stretch(Box("Dim", _root.transform, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.65f), false));
        dim.gameObject.AddComponent<Button>().onClick.AddListener((UnityAction)new Action(() => { }));

        var panel = Box("Panel", _root.transform, new Vector2(1240, 720), Vector2.zero, Panel).transform;
        Box("Accent", panel, new Vector2(1240, 4), new Vector2(0, 358), Lime, false);
        Txt(panel, "Schedule Mods", 40, TextMain, FontStyles.Bold, new Vector2(900, 50), new Vector2(0, 312));
        Txt(panel, "Mods switch on or off the next time you start Schedule I.", 20, TextDim, FontStyles.Normal, new Vector2(900, 30), new Vector2(0, 272));
        Btn(panel, "X", new Vector2(56, 56), new Vector2(574, 312), Soft, TextMain, Close);

        _offTitle = Txt(panel, "", 22, Red, FontStyles.Bold, new Vector2(ListW, 30), new Vector2(-365, 228));
        _onTitle = Txt(panel, "", 22, Lime, FontStyles.Bold, new Vector2(ListW, 30), new Vector2(365, 228));
        _offList = List(panel, new Vector2(-365, -20));
        _onList = List(panel, new Vector2(365, -20));

        Btn(panel, "Enable  >", new Vector2(180, 62), new Vector2(0, 90), Lime, new Color(0.06f, 0.08f, 0.02f), () => Move(true));
        Btn(panel, "<  Disable", new Vector2(180, 62), new Vector2(0, 12), Red, Color.white, () => Move(false));
        Btn(panel, "Info", new Vector2(180, 52), new Vector2(0, -215), Soft, TextMain, () => _info.SetActive(true));
        _hint = Txt(panel, "", 19, TextDim, FontStyles.Italic, new Vector2(1100, 30), new Vector2(0, -312));

        // the Info card, on top of everything
        _info = Stretch(Box("InfoDim", _root.transform, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.55f))).gameObject;
        var card = Box("InfoCard", _info.transform, new Vector2(760, 470), Vector2.zero, Panel).transform;
        Box("Accent", card, new Vector2(760, 4), new Vector2(0, 233), Lime, false);
        Txt(card, "How to use Schedule Mods", 32, TextMain, FontStyles.Bold, new Vector2(680, 44), new Vector2(0, 185));
        var body = Txt(card,
            "•  Mods on the <color=#EE4545>left</color> are switched off. Mods on the <color=#B8F02E>right</color> are switched on.\n" +
            "•  Click a mod to select it. It gets a white outline.\n" +
            "•  Press <b>Enable</b> to move the selected mod to the right, or <b>Disable</b> to move it to the left.\n" +
            "•  Changes take effect the next time you start Schedule I.\n" +
            "•  ScheduleMods Utils is needed by the other mods, so it always stays on.\n" +
            "•  Press Esc or X to close this panel.",
            21, TextMain, FontStyles.Normal, new Vector2(660, 260), new Vector2(0, 10));
        body.alignment = TextAlignmentOptions.TopLeft;
        Btn(card, "Got it", new Vector2(200, 56), new Vector2(0, -180), Lime, new Color(0.06f, 0.08f, 0.02f), () => _info.SetActive(false));
    }

    // a scrolling list with a dark well behind it; returns the content the cards go into
    RectTransform List(Transform parent, Vector2 pos)
    {
        var well = Box("List", parent, new Vector2(ListW, ListH), pos, Well);
        var viewport = Stretch(Make("Viewport", well.transform));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.offsetMin = new Vector2(0, 6); viewport.offsetMax = new Vector2(0, -6);

        var content = Make("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 6, 6);
        layout.spacing = 10;
        layout.childControlWidth = true; layout.childForceExpandWidth = true;
        layout.childControlHeight = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = well.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    // ---------- contents ----------
    // Mods the panel lists: everything in the Mods folder except S1API and S1MAPI, libraries Schedule Loader manages.
    static List<ModEntry> Mods() => ModTools.GetMods().Where(m => !m.Name.StartsWith("S1API") && !m.Name.StartsWith("S1MAPI")).OrderBy(m => Info(m.Name).Name).ToList();

    static (string Name, string Author, string Thumb) Info(string file)
    {
        if (Known.TryGetValue(file, out var k)) return k;
        var loaded = MelonMod.RegisteredMelons.FirstOrDefault(m => m.MelonAssembly != null &&
            Path.GetFileNameWithoutExtension(m.MelonAssembly.Location).Equals(file, StringComparison.OrdinalIgnoreCase));
        return loaded != null ? (loaded.Info.Name, loaded.Info.Author, null) : (file, "Unknown author", null);
    }

    void Refresh()
    {
        foreach (var list in new[] { _offList, _onList })
            for (int i = list.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(list.GetChild(i).gameObject);

        var mods = Mods();
        if (mods.All(m => m.Name != _selected)) _selected = null;
        foreach (var m in mods) Card(m.Enabled ? _onList : _offList, m);
        int on = mods.Count(m => m.Enabled);
        _offTitle.text = $"Disabled  ·  {mods.Count - on}";
        _onTitle.text = $"Enabled  ·  {on}";
        if (_hint.text.Length == 0) _hint.text = "Select a mod, then press Enable or Disable.";
    }

    void Card(RectTransform list, ModEntry m)
    {
        var info = Info(m.Name);
        bool selected = m.Name == _selected;

        // the outer box is the outline: white when selected
        var outline = Box(m.Name, list, Vector2.zero, Vector2.zero, selected ? Color.white : CardBg);
        outline.gameObject.AddComponent<LayoutElement>().preferredHeight = CardH;
        var b = outline.gameObject.AddComponent<Button>();
        b.targetGraphic = outline;
        b.onClick.AddListener((UnityAction)new Action(() => { _selected = m.Name; Refresh(); }));

        var inner = Stretch(Box("Inner", outline.transform, Vector2.zero, Vector2.zero, CardBg, false));
        inner.offsetMin = new Vector2(3, 3); inner.offsetMax = new Vector2(-3, -3);

        float left = -(ListW - 24) / 2;
        var thumb = Thumb(info.Thumb);
        if (thumb != null)
        {
            var img = Make("Thumb", inner.transform).gameObject.AddComponent<RawImage>();
            img.texture = thumb;
            img.raycastTarget = false;
            Place(img.rectTransform, new Vector2(78, 78), new Vector2(left + 52, 0));
        }
        else
        {
            var tile = Box("Thumb", inner.transform, new Vector2(78, 78), new Vector2(left + 52, 0), Soft, false);
            Txt(tile.transform, info.Name.Substring(0, 1).ToUpperInvariant(), 38, TextMain, FontStyles.Bold, new Vector2(78, 78), Vector2.zero);
        }

        var name = Txt(inner.transform, info.Name, 24, TextMain, FontStyles.Bold, new Vector2(330, 34), new Vector2(left + 270, 16));
        name.alignment = TextAlignmentOptions.MidlineLeft;
        var by = Txt(inner.transform, m.Required ? $"{info.Author}  ·  always on" : info.Author, 18, TextDim, FontStyles.Normal, new Vector2(330, 28), new Vector2(left + 270, -18));
        by.alignment = TextAlignmentOptions.MidlineLeft;
    }

    void Move(bool on)
    {
        if (_selected == null) { _hint.text = "Click a mod first to select it."; return; }
        var m = Mods().FirstOrDefault(x => x.Name == _selected);
        if (m == null) { Refresh(); return; }
        var name = Info(m.Name).Name;
        if (m.Required && !on) { _hint.text = $"{name} is needed by the other mods, so it always stays on."; return; }
        if (m.Enabled == on) { _hint.text = $"{name} is already {(on ? "enabled" : "disabled")}."; return; }
        try
        {
            ModTools.SetEnabled(m.Name, on);
            _hint.text = $"{name} will be switched {(on ? "on" : "off")} the next time you start Schedule I.";
        }
        catch (Exception e) { _hint.text = $"Could not change {name}: {e.Message}"; }
        Refresh();
    }

    // ---------- small UI helpers ----------
    static Texture2D Thumb(string file)
    {
        if (file == null) return null;
        if (Thumbs.TryGetValue(file, out var t) && t != null) return t;
        var path = Path.Combine(ThumbDir, file);
        if (!File.Exists(path)) return null;
        try
        {
            t = new Texture2D(2, 2);
            if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path))) return null;
            t.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives scene changes
            return Thumbs[file] = t;
        }
        catch { return null; }
    }

    // a white rounded square that stretches without blurring its corners (9-slice), tinted per use
    static Sprite Round()
    {
        if (_round != null) return _round;
        const int n = 32, r = 10;
        var tex = new Texture2D(n, n) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f))); // soft edge
            }
        tex.Apply();
        _round = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
        _round.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _round;
    }

    static RectTransform Make(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static void Place(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    static RectTransform Stretch(Graphic g) => Stretch(g.rectTransform);
    static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Image Box(string name, Transform parent, Vector2 size, Vector2 pos, Color color, bool rounded = true)
    {
        var rt = Make(name, parent);
        Place(rt, size, pos);
        var img = rt.gameObject.AddComponent<Image>();
        if (rounded) { img.sprite = Round(); img.type = Image.Type.Sliced; }
        img.color = color;
        return img;
    }

    static TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, FontStyles style, Vector2 box, Vector2 pos)
    {
        var rt = Make("Text", parent);
        Place(rt, box, pos);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false; // text never blocks clicks
        return t;
    }

    static void Btn(Transform parent, string label, Vector2 size, Vector2 pos, Color fill, Color textColor, Action click)
    {
        var img = Box(label, parent, size, pos, fill);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener((UnityAction)new Action(click));
        Txt(img.transform, label, 22, textColor, FontStyles.Bold, size, Vector2.zero);
    }
}
