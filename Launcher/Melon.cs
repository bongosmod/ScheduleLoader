using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScheduleLoader;

public record Release(string Tag, string Url, string? Sha);

/// <summary>MelonLoader: verified download, staged install with rollback, auto-update when older, clean removal.</summary>
static class Melon
{
    const string Api = "https://api.github.com/repos/LavaGang/MelonLoader/releases/latest";
    const string Fallback = "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip";
    static string Proxy => Path.Combine(Game.Dir, "version.dll");
    static string Folder => Path.Combine(Game.Dir, "MelonLoader");
    static string Manifest => Path.Combine(Paths.App, "melonloader-files.txt");
    static string VersionFile => Path.Combine(Paths.App, "melonloader-version.txt");

    public static bool Installed => File.Exists(Proxy) && Directory.Exists(Folder);
    public static bool InstalledByUs => File.Exists(Manifest); // the list of files we added exists only if we installed it

    public static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ScheduleLoader");
        return h;
    }

    // ---------- versions ----------
    static Version? Parse(string? s)
    {
        if (s == null) return null;
        s = s.Trim().TrimStart('v', 'V');
        int i = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (i > 0) s = s[..i];
        return Version.TryParse(s, out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0)) : null;
    }

    public static string? InstalledVersion()
    {
        if (File.Exists(VersionFile)) return File.ReadAllText(VersionFile).Trim();
        var dll = Path.Combine(Folder, "net6", "MelonLoader.dll");
        return File.Exists(dll) ? FileVersionInfo.GetVersionInfo(dll).ProductVersion : null;
    }

    // unknown versions are left alone: we only replace what we can prove is older
    public static bool IsOlder(string? have, string latest) => Parse(have) is Version a && Parse(latest) is Version b && a < b;

    // ---------- latest release (null when offline) ----------
    public static async Task<Release?> LatestAsync()
    {
        try
        {
            using var http = Http();
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
            string tag = doc.RootElement.GetProperty("tag_name").GetString()!;
            var assets = doc.RootElement.GetProperty("assets").EnumerateArray().Select(a => a.Clone()).ToList();
            static string Name(JsonElement a) => a.GetProperty("name").GetString()!.ToLowerInvariant();
            var asset = assets.FirstOrDefault(a => Name(a) == "melonloader.x64.zip");
            if (asset.ValueKind == JsonValueKind.Undefined)
                asset = assets.FirstOrDefault(a => Name(a).Contains("x64") && Name(a).EndsWith(".zip") && !Name(a).Contains("linux"));
            if (asset.ValueKind == JsonValueKind.Undefined) return new Release(tag, Fallback, null);

            string? sha = null;
            if (asset.TryGetProperty("digest", out var d) && d.GetString() is string dg && dg.StartsWith("sha256:")) sha = dg[7..];
            return new Release(tag, asset.GetProperty("browser_download_url").GetString()!, sha);
        }
        catch (Exception ex) { Logger.Write("Update check failed: " + ex.Message); return null; }
    }

    // ---------- install / update ----------
    public static async Task EnsureAsync(Release? rel, Action<string> say)
    {
        if (Installed)
        {
            if (rel == null) { say("Could not check for updates, keeping the installed MelonLoader."); return; }
            if (!IsOlder(InstalledVersion(), rel.Tag)) { say("MelonLoader is up to date."); return; }
            say($"Updating MelonLoader to {rel.Tag}…");
            Remove();
        }
        await InstallAsync(rel ?? new Release("unknown", Fallback, null), say);
    }

    static async Task InstallAsync(Release rel, Action<string> say)
    {
        say("Downloading MelonLoader…");
        byte[] bytes;
        try { using var http = Http(); bytes = await http.GetByteArrayAsync(rel.Url); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not download MelonLoader ({ex.Message}). Check your internet connection, or install it manually from github.com/LavaGang/MelonLoader/releases and click Set up again.");
        }
        Logger.Write($"Downloaded {rel.Url} ({bytes.Length} bytes)");
        if (rel.Sha != null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(rel.Sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The download failed its integrity check. Nothing was installed.");

        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(bytes)); }
        catch { throw new InvalidOperationException("The download was not a valid zip file. Nothing was installed."); }

        using (zip)
        {
            if (!zip.Entries.Any(e => e.FullName.Equals("version.dll", StringComparison.OrdinalIgnoreCase)) || !zip.Entries.Any(e => e.FullName.StartsWith("MelonLoader/")))
                throw new InvalidOperationException("The download is not a MelonLoader package. Nothing was installed.");

            var stage = Path.Combine(Path.GetTempPath(), "ScheduleLoader-" + Guid.NewGuid().ToString("N"));
            var added = new List<string>();
            string? backup = null;
            try
            {
                zip.ExtractToDirectory(stage);
                if (File.Exists(Proxy) && !Directory.Exists(Folder)) { backup = Proxy + ".sl-backup"; File.Move(Proxy, backup, true); } // someone else's version.dll

                say("Installing MelonLoader…");
                foreach (var f in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(Game.Dir, Path.GetRelativePath(stage, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (!File.Exists(dest)) added.Add(dest);
                    File.Copy(f, dest, true);
                }
                if (!Installed) throw new InvalidOperationException("MelonLoader files are missing after the copy.");
                File.WriteAllLines(Manifest, added);
                File.WriteAllText(VersionFile, rel.Tag);
                Logger.Write($"MelonLoader {rel.Tag} installed ({added.Count} files)");
            }
            catch
            {
                foreach (var f in added) try { File.Delete(f); } catch { }
                if (backup != null && File.Exists(backup)) File.Move(backup, Proxy, true);
                Logger.Write("MelonLoader install rolled back");
                throw;
            }
            finally { try { Directory.Delete(stage, true); } catch { } }
        }
    }

    // ---------- removal (used by updates and by Uninstall) ----------
    public static void Remove()
    {
        string root = Path.GetFullPath(Game.Dir);
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);

        var files = File.Exists(Manifest) ? File.ReadAllLines(Manifest) : new[] { Proxy };
        foreach (var f in files.Append(Proxy))
        {
            // never touch anything outside the game folder
            if (Path.GetFullPath(f).StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(f)) File.Delete(f);
        }
        if (File.Exists(Proxy + ".sl-backup")) File.Move(Proxy + ".sl-backup", Proxy, true); // give back the version.dll we set aside

        File.Delete(Manifest);
        File.Delete(VersionFile);
        Logger.Write("MelonLoader removed");
    }
}
