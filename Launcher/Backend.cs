using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScheduleLoader;

public record ModInfo(string Name, string File, string Author, string Description, string Version,
                      bool Verified, bool Custom = false, bool Required = false, string? Thumb = null,
                      bool Auto = true,        // built during Set up and hidden from the Browser; false = an optional download in the Browser
                      string? Url = null, string? Sha = null); // set for mods from the online catalog: a finished DLL and its checksum

static class Paths
{
    // everything the app keeps (library, logs, built-in files) lives here, so the exe can be a single file anywhere
    public static readonly string App = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLoader")).FullName;
}

/// <summary>Finds, starts and closes Schedule I.</summary>
static class Game
{
    static string Cfg => Path.Combine(Paths.App, "gamepath.txt");
    public static string Dir = "";
    public static string Exe => Path.Combine(Dir, "Schedule I.exe");
    public static bool Found => File.Exists(Exe);
    public static bool Il2Cpp => File.Exists(Path.Combine(Dir, "GameAssembly.dll"));
    public static Process? Running => Process.GetProcessesByName("Schedule I").FirstOrDefault();

    static Game()
    {
        if (File.Exists(Cfg)) Dir = File.ReadAllText(Cfg).Trim();
        if (!Found) Dir = Detect() ?? Dir;
    }

    public static void Set(string dir) { Dir = dir; File.WriteAllText(Cfg, dir); }
    public static void Start() => Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = Dir, UseShellExecute = true });

    public static void Close()
    {
        var p = Running;
        if (p == null) return;
        p.CloseMainWindow();
        if (!p.WaitForExit(8000)) p.Kill();
    }

    // Steam install + every Steam library folder
    static string? Detect()
    {
        var libs = new List<string> { @"C:\Program Files (x86)\Steam" };
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string s)
        {
            s = s.Replace('/', '\\');
            libs.Add(s);
            var vdf = Path.Combine(s, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                libs.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace("\\\\", "\\")));
        }
        return libs.Select(l => Path.Combine(l, "steamapps", "common", "Schedule I")).FirstOrDefault(d => File.Exists(Path.Combine(d, "Schedule I.exe")));
    }
}

/// <summary>The mod library: DLL present = enabled, DLL.disabled = disabled. Mirrors into the game's Mods folder.</summary>
static class Store
{
    // the catalog.json in Mods, built into the exe; replaced by the online catalog once it loads (see Online)
    public static ModInfo[] Catalog { get; private set; } = Online.Builtin();
    public static void SetCatalog(ModInfo[] mods) => Catalog = mods;

    public static string Lib => Directory.CreateDirectory(Path.Combine(Paths.App, "Library")).FullName;
    static string P(ModInfo m) => Path.Combine(Lib, m.File);
    static string GameMods => Path.Combine(Game.Dir, "Mods");

    public static bool Has(ModInfo m) => File.Exists(P(m)) || File.Exists(P(m) + ".disabled");
    public static bool On(ModInfo m) => File.Exists(P(m));

    public static void Set(ModInfo m, bool on)
    {
        if (m.Required || !Has(m)) return;
        var (src, dst) = on ? (P(m) + ".disabled", P(m)) : (P(m), P(m) + ".disabled");
        if (File.Exists(src)) File.Move(src, dst, true);
    }

    public static List<ModInfo> Library() => Directory.GetFiles(Lib)
        .Select(f => Path.GetFileName(f).Replace(".disabled", ""))
        .Where(n => n.EndsWith(".dll")).Distinct()
        .Select(n => Catalog.FirstOrDefault(c => c.File == n) ?? new ModInfo(n[..^4], n, "User", "Custom uploaded mod.", "?", false, true))
        .OrderBy(m => m.Custom).ThenBy(m => Array.IndexOf(Catalog, m)).ToList();


    public static void Remove(ModInfo m) { foreach (var ext in new[] { "", ".disabled", ".stamp", ".version" }) File.Delete(P(m) + ext); }

    // pick up toggles made in-game
    public static void Pull()
    {
        if (!Game.Found) return;
        foreach (var m in Library().Where(m => !m.Required))
        {
            var g = Path.Combine(GameMods, m.File);
            if (File.Exists(g + ".disabled")) Set(m, false);
            else if (File.Exists(g)) Set(m, true);
        }
    }

    // copy the library into the game right before launch
    public static void Push()
    {
        Directory.CreateDirectory(GameMods);
        foreach (var m in Library())
        {
            var g = Path.Combine(GameMods, m.File);
            bool on = On(m);
            File.Delete(on ? g + ".disabled" : g);
            File.Copy(on ? P(m) : P(m) + ".disabled", on ? g : g + ".disabled", true);
        }
    }
}

/// <summary>Set up (install or update only what is needed), build mods, and uninstall.</summary>
static class Setup
{
    static string Asm => Path.Combine(Game.Dir, "MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll");

    public static bool Done => Game.Found && Melon.Installed && File.Exists(Asm) && Store.Library().Any(m => m.Required);

    // returns a warning if some mods could not be built, null when everything worked
    public static async Task<string?> RunAsync(Action<string> say)
    {
        if (!Game.Found) throw new InvalidOperationException("Schedule I not found. Install it on Steam, or use the folder button to point to it.");
        if (!Game.Il2Cpp) throw new InvalidOperationException("Schedule Loader supports the main version of Schedule I (not the alternate branch).");
        if (Game.Running != null) throw new InvalidOperationException("Close Schedule I first, then try again.");

        say("Checking MelonLoader…");
        await Melon.EnsureAsync(await Melon.LatestAsync(), say); // skips if present and current, replaces if older

        if (!File.Exists(Asm))
        {
            say("Starting the game once to prepare it. This can take several minutes…");
            Game.Start();
            for (int i = 0; i < 900 && !File.Exists(Asm); i++)
            {
                await Task.Delay(2000);
                if (i > 10 && Game.Running == null) break;
            }
            if (!File.Exists(Asm)) throw new InvalidOperationException("The game closed before preparation finished. Click Set up again.");
            await Task.Delay(15000);
            Game.Close();
        }

        // the modding libraries (S1API, S1MAPI) first: some mods are built against them. A failed one must not stop the rest.
        var failed = await Deps.EnsureAllAsync(say);

        // one mod failing to build must not stop the others
        foreach (var m in Store.Catalog.Where(m => m.Auto || Store.Has(m))) // optional mods only if installed
        {
            try { await BuildAsync(m, say, force: false); }
            catch (Exception ex) { Logger.Write($"Build failed for {m.Name}: {ex}"); failed.Add(m.Name); }
        }
        return failed.Count == 0 ? null : "Set up finished, but these could not be set up: " + string.Join(", ", failed) + ". Details: " + Logger.LogPath;
    }

    public static async Task BuildAsync(ModInfo m, Action<string> say, bool force = true)
    {
        if (!File.Exists(Asm)) throw new InvalidOperationException("Run Set up Schedule Loader first.");
        if (m.Url != null) { await Online.InstallAsync(m, say, force); return; } // a finished DLL from the online catalog: nothing to build

        string name = Path.GetFileNameWithoutExtension(m.File);
        string dll = Path.Combine(Store.Lib, m.File), stampFile = dll + ".stamp";
        string stamp = $"{SourceHash()}|{Melon.InstalledVersion()}|{GameStamp()}"; // changes when our code, MelonLoader or the game changes

        if (!force && Store.Has(m) && File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp) { say($"{m.Name} is up to date."); return; }

        var dotnet = await DotnetAsync(say);
        say($"Building {m.Name}…");
        bool wasOff = File.Exists(dll + ".disabled") && !File.Exists(dll);
        var proj = Path.Combine(Paths.App, "src", name, name + ".csproj");
        (int code, string output) = await Run(dotnet, $"build \"{proj}\" -c Release -p:GameDir=\"{Game.Dir}\" -o \"{Store.Lib}\" --nologo -v q");
        if (code != 0)
        {
            File.WriteAllText(Path.Combine(Paths.App, $"build-{name}.log"), output);
            Logger.Write($"Build of {m.Name} failed:\n{output}");
            throw new InvalidOperationException($"Could not build {m.Name}:\n{Summary(output)}");
        }

        File.WriteAllText(stampFile, stamp);
        // a mod you switched off stays off after an update
        if (wasOff) File.Move(dll, dll + ".disabled", true);
    }

    // Removes everything Schedule Loader put on this PC: its mods, MelonLoader, S1API and S1MAPI from the game folder, the mods'
    // settings, and the app's own library of installed mods, so the next Set up starts clean. Game saves are never touched.
    public static string Uninstall()
    {
        if (!Game.Found) throw new InvalidOperationException("Schedule I not found.");
        if (Game.Running != null) throw new InvalidOperationException("Close Schedule I first, then try again.");

        int removed = 0;
        string mods = Path.Combine(Game.Dir, "Mods"), userData = Path.Combine(Game.Dir, "UserData");
        foreach (var m in Store.Catalog.Concat(Store.Library()).Distinct())
            foreach (var f in new[] { Path.Combine(mods, m.File), Path.Combine(mods, m.File + ".disabled") })
                if (File.Exists(f)) { File.Delete(f); removed++; }

        // mod settings: TV+ keeps its uploaded wallpaper in UserData\TVPlus and its choices in MelonLoader's settings file
        var tvPlus = Path.Combine(userData, "TVPlus");
        if (Directory.Exists(tvPlus)) Directory.Delete(tvPlus, true);
        bool ourMelon = Melon.InstalledByUs; // read before Remove, which forgets it
        var prefs = Path.Combine(userData, "MelonPreferences.cfg");
        if (ourMelon)
        {
            // we installed MelonLoader, so its own settings files are ours to remove too
            foreach (var f in new[] { prefs, Path.Combine(userData, "Loader.cfg") }) File.Delete(f);
        }
        else if (File.Exists(prefs)) File.WriteAllText(prefs, WithoutSection(File.ReadAllText(prefs), "TVPlus")); // keep other mods' settings

        Deps.RemoveAll();
        Uploads.RemoveExtras();
        Melon.Remove();

        // leave folders alone if they still hold someone else's files
        foreach (var d in new[] { mods, Path.Combine(Game.Dir, "Plugins"), userData })
            if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);

        // the app's own data: installed mods (including custom uploads), helper tools, build logs, the remembered game folder.
        // The built-in files this open window is using (src, Assets, the log) are left; the exe puts them back anyway.
        int mods2 = Store.Library().Count;
        foreach (var d in new[] { Store.Lib, Path.Combine(Paths.App, "tools") })
            if (Directory.Exists(d)) Directory.Delete(d, true);
        foreach (var f in Directory.GetFiles(Paths.App, "build-*.log").Append(Path.Combine(Paths.App, "gamepath.txt"))) File.Delete(f);

        Logger.Write($"Uninstalled ({removed} mod files in the game, {mods2} mods in the library)");
        return $"Uninstalled everything: MelonLoader, S1API, S1MAPI, {removed} mod file(s) in the game, {mods2} installed mod(s) and their settings. Your saves were not touched.";
    }

    // a MelonLoader settings file with one [Section] (and its lines) taken out
    static string WithoutSection(string cfg, string section)
    {
        var keep = new List<string>();
        bool skipping = false;
        foreach (var line in cfg.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("[")) skipping = t == $"[{section}]";
            if (!skipping) keep.Add(line);
        }
        return string.Join("\n", keep);
    }

    // ---------- helpers ----------
    // the compiler errors only, without long file paths
    static string Summary(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(": error ") || l.StartsWith("error "))
            .Select(l => Regex.Replace(l, @"^.*\\(?=[^\\]*\(\d+,\d+\))", ""))
            .Select(l => Regex.Replace(l, @"\s*\[[^\]]*\]$", ""))
            .Distinct().Take(6).ToList();
        var all = output.Trim();
        return lines.Count > 0 ? string.Join("\n", lines) : all[^Math.Min(400, all.Length)..];
    }

    static string SourceHash()
    {
        using var sha = SHA256.Create();
        var files = Directory.GetFiles(Path.Combine(Paths.App, "src"), "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\")).OrderBy(f => f);
        foreach (var f in files) { var b = File.ReadAllBytes(f); sha.TransformBlock(b, 0, b.Length, null, 0); }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    static string GameStamp()
    {
        var f = new FileInfo(Path.Combine(Game.Dir, "GameAssembly.dll"));
        return f.Exists ? $"{f.Length}-{f.LastWriteTimeUtc.Ticks}" : "none";
    }

    // finds the .NET SDK, installing it through winget if it is missing
    static async Task<string> DotnetAsync(Action<string> say)
    {
        string[] paths = { "dotnet", @"C:\Program Files\dotnet\dotnet.exe" };
        async Task<string?> Find()
        {
            foreach (var d in paths)
            {
                var (c, o) = await Run(d, "--list-sdks");
                if (c == 0 && o.Trim().Length > 0) return d;
            }
            return null;
        }
        var found = await Find();
        if (found != null) return found;

        say("Installing the .NET SDK (needed to build mods)…");
        await Run("winget", "install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements");
        return await Find() ?? throw new InvalidOperationException("Could not install the .NET SDK automatically. Install it from dotnet.microsoft.com/download, then click Set up again.");
    }

    static async Task<(int, string)> Run(string file, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, await o + await e);
        }
        catch { return (-1, ""); }
    }
}
