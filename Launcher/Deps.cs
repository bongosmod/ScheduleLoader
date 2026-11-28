using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScheduleLoader;

/// <summary>
/// Modding libraries every setup gets, always from their official GitHub releases (ifBars):
/// S1API (adds TV apps and more) and S1MAPI (custom meshes, props and buildings).
/// Each is skipped if present and current, updated if older, and removed by Uninstall.
/// </summary>
static class Deps
{
    record Helper(string Key, string Name, string Repo, Func<string, bool> Pick);

    // asset names are lower-cased before Pick sees them
    static readonly Helper S1Api = new("s1api", "S1API", "ifBars/S1API",
        n => n.EndsWith(".zip") && n.StartsWith("s1api") && !n.Contains("thunderstore") && !n.Contains("mono"));
    static readonly Helper S1Mapi = new("s1mapi", "S1MAPI", "ifBars/S1MAPI",
        n => n.EndsWith("_il2cpp.dll") && n.StartsWith("s1mapi"));
    static readonly Helper[] All = { S1Api, S1Mapi };

    static string Manifest(Helper h) => Path.Combine(Paths.App, h.Key + "-files.txt");
    static string VersionFile(Helper h) => Path.Combine(Paths.App, h.Key + "-version.txt");

    public static bool S1ApiPresent => Present("S1API*.dll");
    static bool Present(string pattern) => new[] { "Mods", "Plugins" }.Any(d =>
        Directory.Exists(Path.Combine(Game.Dir, d)) && Directory.GetFiles(Path.Combine(Game.Dir, d), pattern).Length > 0);

    /// <summary>Installs or updates every library. Returns the names of any that could not be set up.</summary>
    public static async Task<List<string>> EnsureAllAsync(Action<string> say)
    {
        var failed = new List<string>();
        foreach (var h in All)
            try { await EnsureAsync(h, say); }
            catch (Exception ex) { Logger.Write($"{h.Name} setup failed: {ex}"); failed.Add(h.Name); }
        return failed;
    }

    public static void RemoveAll() { foreach (var h in All) Remove(h); }

    static async Task<Release?> LatestAsync(Helper h)
    {
        try
        {
            using var http = Melon.Http();
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{h.Repo}/releases/latest"));
            string tag = doc.RootElement.GetProperty("tag_name").GetString()!;
            static string N(JsonElement a) => a.GetProperty("name").GetString()!.ToLowerInvariant();
            var pick = doc.RootElement.GetProperty("assets").EnumerateArray().Select(a => a.Clone())
                .Where(a => h.Pick(N(a))).OrderByDescending(a => N(a).Contains("il2cpp")).FirstOrDefault();
            if (pick.ValueKind == JsonValueKind.Undefined) return null;

            string? sha = null;
            if (pick.TryGetProperty("digest", out var d) && d.GetString() is string dg && dg.StartsWith("sha256:")) sha = dg[7..];
            var url = pick.GetProperty("browser_download_url").GetString()!;
            // only ever the project's own GitHub release files
            if (!url.StartsWith($"https://github.com/{h.Repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) return null;
            return new Release(tag, url, sha);
        }
        catch (Exception ex) { Logger.Write($"{h.Name} lookup failed: " + ex.Message); return null; }
    }

    static async Task EnsureAsync(Helper h, Action<string> say)
    {
        say($"Checking {h.Name}…");
        var rel = await LatestAsync(h);
        if (Present(h.Name + "*.dll"))
        {
            var have = File.Exists(VersionFile(h)) ? File.ReadAllText(VersionFile(h)).Trim() : null;
            // a copy we did not install, or no way to check online: leave it alone
            if (have == null || rel == null || !Melon.IsOlder(have, rel.Tag)) { say($"{h.Name} is ready."); return; }
            say($"Updating {h.Name} to {rel.Tag}…");
            Remove(h);
        }
        if (rel == null) throw new InvalidOperationException($"Could not find {h.Name} online. Check your internet connection and try again.");

        say($"Downloading {h.Name}…");
        byte[] bytes;
        try { using var http = Melon.Http(); bytes = await http.GetByteArrayAsync(rel.Url); }
        catch (Exception ex) { throw new InvalidOperationException($"Could not download {h.Name} ({ex.Message}). Check your internet connection and try again."); }
        if (rel.Sha != null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(rel.Sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The {h.Name} download failed its integrity check. Nothing was installed.");

        say($"Installing {h.Name}…");
        var added = new List<string>();
        try
        {
            if (rel.Url.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) InstallDll(h, bytes, rel.Url, added);
            else InstallZip(h, bytes, added);
            File.WriteAllLines(Manifest(h), added);
            File.WriteAllText(VersionFile(h), rel.Tag);
            Logger.Write($"{h.Name} {rel.Tag} installed ({added.Count} files)");
        }
        catch
        {
            foreach (var f in added) try { File.Delete(f); } catch { }
            Logger.Write($"{h.Name} install rolled back");
            throw;
        }
    }

    // a single MelonLoader mod file: goes in Mods
    static void InstallDll(Helper h, byte[] bytes, string url, List<string> added)
    {
        if (bytes.Length < 1024 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new InvalidOperationException($"The {h.Name} download was not a valid mod file. Nothing was installed.");
        var dest = Path.Combine(Game.Dir, "Mods", Path.GetFileName(new Uri(url).LocalPath));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, bytes);
        added.Add(dest);
    }

    // a zip laid out like the game folder: only its Mods and Plugins files are copied
    static void InstallZip(Helper h, byte[] bytes, List<string> added)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(bytes)); }
        catch { throw new InvalidOperationException($"The {h.Name} download was not a valid zip file. Nothing was installed."); }
        using (zip)
        {
            static bool Wanted(string n) => n.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase);
            if (!zip.Entries.Any(e => Wanted(e.FullName) && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The {h.Name} download did not look right. Nothing was installed.");
            string root = Path.GetFullPath(Game.Dir) + Path.DirectorySeparatorChar;
            foreach (var e in zip.Entries)
            {
                var rel = e.FullName.Replace('\\', '/');
                if (e.Name.Length == 0 || !Wanted(rel)) continue;
                var dest = Path.GetFullPath(Path.Combine(Game.Dir, rel));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never outside the game folder
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, true);
                added.Add(dest);
            }
        }
    }

    // removes only what we installed (never outside the game folder)
    static void Remove(Helper h)
    {
        if (!File.Exists(Manifest(h))) return;
        string root = Path.GetFullPath(Game.Dir);
        foreach (var f in File.ReadAllLines(Manifest(h)))
            if (Path.GetFullPath(f).StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(f)) File.Delete(f);
        File.Delete(Manifest(h));
        File.Delete(VersionFile(h));
        Logger.Write($"{h.Name} removed");
    }
}
