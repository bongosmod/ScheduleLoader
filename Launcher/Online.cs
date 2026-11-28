using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScheduleLoader;

/// <summary>
/// The mod list: Mods\catalog.json, built into the exe, and (when it names a GitHub repo) the catalog.json in that
/// repo's latest release, which wins. Mods from the online catalog are downloaded as finished DLLs from that release,
/// each checked against the checksum GitHub publishes, so mod updates need no new launcher.
/// </summary>
static class Online
{
    class CatalogFile { public string? Repo { get; set; } public List<Entry> Mods { get; set; } = new(); }
    class Entry
    {
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "0";
        public string? Thumb { get; set; }
        public bool Required { get; set; }
        public bool Optional { get; set; } // true = only from the Browser; false = installed by Set up
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The GitHub repository ("owner/name") whose releases hold the mods; empty until one is set in catalog.json.</summary>
    public static string Repo { get; private set; } = "";
    static string ReleasePrefix => $"https://github.com/{Repo}/releases/download/";

    /// <summary>The list built into this exe (the offline fallback).</summary>
    public static ModInfo[] Builtin()
    {
        try
        {
            var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(Path.Combine(Paths.App, "src", "catalog.json")), Json)!;
            Repo = ValidRepo(file.Repo) ? file.Repo!.Trim() : "";
            return file.Mods.Where(Valid).Select(e => ToMod(e, null, null)).ToArray();
        }
        catch (Exception ex) { Logger.Write("Built-in catalog unreadable: " + ex.Message); return Array.Empty<ModInfo>(); }
    }

    /// <summary>Fetches the latest release's catalog and thumbnails. Returns true when the mod list changed.</summary>
    public static async Task<bool> RefreshAsync()
    {
        if (Repo.Length == 0) return false;
        try
        {
            using var http = Melon.Http();
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToDictionary(
                a => a.GetProperty("name").GetString()!,
                a => (Url: a.GetProperty("browser_download_url").GetString()!,
                      Sha: a.TryGetProperty("digest", out var d) && d.GetString() is string s && s.StartsWith("sha256:") ? s[7..] : null),
                StringComparer.OrdinalIgnoreCase);
            if (!assets.TryGetValue("catalog.json", out var cat)) { Logger.Write($"Online catalog: the latest release of {Repo} has no catalog.json"); return false; }

            var file = JsonSerializer.Deserialize<CatalogFile>(await DownloadAsync(http, cat.Url, cat.Sha, "catalog.json"), Json)!;
            var online = new List<ModInfo>();
            foreach (var e in file.Mods.Where(Valid))
            {
                if (!assets.TryGetValue(e.File, out var dll)) { Logger.Write($"Online catalog: {e.File} is listed but not in the release"); continue; }
                if (e.Thumb != null && assets.TryGetValue(e.Thumb, out var th))
                    try { File.WriteAllBytes(Path.Combine(Paths.App, "Assets", e.Thumb), await DownloadAsync(http, th.Url, th.Sha, e.Thumb)); }
                    catch (Exception ex) { Logger.Write($"Thumbnail {e.Thumb} not downloaded: {ex.Message}"); }
                online.Add(ToMod(e, dll.Url, dll.Sha));
            }
            if (online.Count == 0) return false;
            // online entries win; built-in ones the online catalog does not have stay available (built from source)
            Store.SetCatalog(online.Concat(Store.Catalog.Where(b => online.All(o => !o.File.Equals(b.File, StringComparison.OrdinalIgnoreCase)))).ToArray());
            Ui.ForgetImages();
            Logger.Write($"Online catalog loaded from {Repo}: {online.Count} mods");
            return true;
        }
        catch (Exception ex) { Logger.Write("Online catalog not loaded (offline, or no release yet): " + ex.Message); return false; }
    }

    /// <summary>Installs or updates a mod from the online catalog. A mod you switched off stays off.</summary>
    public static async Task InstallAsync(ModInfo m, Action<string> say, bool force)
    {
        string dll = Path.Combine(Store.Lib, m.File), versionFile = dll + ".version";
        if (!force && Store.Has(m) && File.Exists(versionFile) && File.ReadAllText(versionFile).Trim() == m.Version) { say($"{m.Name} is up to date."); return; }

        say($"Downloading {m.Name} {m.Version}…");
        byte[] bytes;
        using (var http = Melon.Http()) bytes = await DownloadAsync(http, m.Url!, m.Sha, m.File);
        var problem = Uploads.Check(bytes);
        if (problem != null) throw new InvalidOperationException($"The download of {m.Name} was refused: {problem}.");

        bool off = File.Exists(dll + ".disabled") && !File.Exists(dll);
        File.WriteAllBytes(off ? dll + ".disabled" : dll, bytes);
        File.Delete(off ? dll : dll + ".disabled");
        File.Delete(dll + ".stamp"); // no longer a locally built copy
        File.WriteAllText(versionFile, m.Version);
        Logger.Write($"{m.Name} {m.Version} installed from {Repo}");
    }

    // only this repo's own release files, and only when GitHub's checksum matches
    static async Task<byte[]> DownloadAsync(HttpClient http, string url, string? sha, string what)
    {
        if (!url.StartsWith(ReleasePrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"{what} is not a release file of {Repo}.");
        if (sha == null) throw new InvalidOperationException($"GitHub gave no checksum for {what}, so it was not downloaded.");
        var bytes = await http.GetByteArrayAsync(url);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{what} failed its integrity check, so it was not installed.");
        return bytes;
    }

    static ModInfo ToMod(Entry e, string? url, string? sha) =>
        new(e.Name, e.File, e.Author, e.Description, e.Version, true, Required: e.Required, Thumb: e.Thumb, Auto: !e.Optional, Url: url, Sha: sha);

    // file names only (never folders), so nothing in a catalog can point outside the app's own folders
    static bool Valid(Entry e) => Plain(e.File, ".dll") && e.Name.Length > 0 && (e.Thumb == null || Plain(e.Thumb, ".png") || Plain(e.Thumb, ".jpg"));
    static bool Plain(string? f, string ext) => !string.IsNullOrWhiteSpace(f) && Path.GetFileName(f) == f && f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && f.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    static bool ValidRepo(string? r) => r != null && System.Text.RegularExpressions.Regex.IsMatch(r.Trim(), @"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$");
}
