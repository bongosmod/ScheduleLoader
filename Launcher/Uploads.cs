using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace ScheduleLoader;

/// <summary>
/// Custom mods the user adds: a .dll, or a .zip as downloaded from mod sites like Nexus Mods.
/// Mod DLLs go into the Library (so they can be switched on and off); a zip's other game files
/// (UserLibs, Plugins, UserData, Mods sub-folders) are copied straight into the game folder and removed by Uninstall.
/// </summary>
static class Uploads
{
    static string Manifest => Path.Combine(Paths.App, "custom-files.txt");
    static readonly string[] GameFolders = { "Mods", "UserLibs", "Plugins", "UserData" };

    /// <summary>Adds one picked file. Returns a line per mod or group of files, for the user.</summary>
    public static List<string> Add(string path)
    {
        var report = new List<string>();
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) AddZip(path, report);
        else AddDll(File.ReadAllBytes(path), Path.GetFileName(path), report);
        return report;
    }

    static void AddDll(byte[] bytes, string name, List<string> report)
    {
        var problem = Check(bytes);
        if (problem != null) { report.Add($"{name}: not added, {problem}."); return; }
        if (Store.Catalog.Any(c => c.File.Equals(name, StringComparison.OrdinalIgnoreCase))) { report.Add($"{name}: not added, it has the same name as a built-in mod."); return; }
        File.Delete(Path.Combine(Store.Lib, name + ".disabled")); // a fresh upload replaces an old copy, switched on
        File.WriteAllBytes(Path.Combine(Store.Lib, name), bytes);
        report.Add($"{name}: added to your Library.");
    }

    /// <summary>Why a mod DLL will not work in this game, from the libraries it was built against; null when it looks right.</summary>
    internal static string? Check(byte[] bytes)
    {
        try
        {
            using var pe = new PEReader(new MemoryStream(bytes));
            if (!pe.HasMetadata) return "it is not a mod file";
            var md = pe.GetMetadataReader();
            var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
            if (refs.Any(r => r.StartsWith("BepInEx"))) return "it is a BepInEx mod, and Schedule Loader uses MelonLoader";
            if (!refs.Contains("MelonLoader")) return "it is not a MelonLoader mod";
            bool game = refs.Contains("Assembly-CSharp") || refs.Any(r => r.StartsWith("UnityEngine"));
            if (game && !refs.Any(r => r.StartsWith("Il2Cpp"))) return "it is the Mono version (for the alternate branch). Download the IL2CPP version instead";
            return null;
        }
        catch { return "it is not a valid mod file"; }
    }

    static void AddZip(string path, List<string> report)
    {
        using var zip = ZipFile.OpenRead(path);
        var layout = Layout(zip.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName));
        var byPath = zip.Entries.Where(e => e.Name.Length > 0).ToDictionary(e => e.FullName);
        var mods = layout.Where(kv => IsModDll(kv.Value)).ToList();
        if (mods.Count == 0) { report.Add($"{Path.GetFileName(path)}: no IL2CPP MelonLoader mod found inside."); return; }

        foreach (var (entry, rel) in mods)
        {
            using var s = byPath[entry].Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            AddDll(ms.ToArray(), Path.GetFileName(rel), report);
        }

        var extras = layout.Where(kv => !IsModDll(kv.Value) && kv.Value.Contains('/') && GameFolders.Contains(kv.Value.Split('/')[0], StringComparer.OrdinalIgnoreCase)).ToList();
        if (extras.Count == 0) return;
        if (!Game.Found) { report.Add($"{extras.Count} extra file(s) skipped: Schedule I was not found."); return; }
        string root = Path.GetFullPath(Game.Dir) + Path.DirectorySeparatorChar;
        var added = new List<string>();
        foreach (var (entry, rel) in extras)
        {
            var dest = Path.GetFullPath(Path.Combine(Game.Dir, rel));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never outside the game folder
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            byPath[entry].ExtractToFile(dest, true);
            added.Add(dest);
        }
        File.AppendAllLines(Manifest, added);
        report.Add($"{added.Count} extra file(s) copied into the game ({string.Join(", ", extras.Select(x => x.Value.Split('/')[0]).Distinct())}).");
    }

    // a mod DLL: loose in the zip, or directly in its Mods folder
    static bool IsModDll(string rel)
    {
        if (!rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = rel.Split('/');
        return parts.Length == 1 || parts.Length == 2 && parts[0].Equals("Mods", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where each zip entry belongs, relative to the game folder. Mod zips often hold both builds ("IL2CPP/Mods/x.dll"
    /// and "Mono/Mods/x.dll") and/or a wrapper folder ("MyMod/Mods/x.dll"): Mono folders are dropped, IL2CPP folders
    /// and a shared wrapper folder are stepped through.
    /// </summary>
    internal static Dictionary<string, string> Layout(IEnumerable<string> entries)
    {
        var map = new Dictionary<string, string>();
        foreach (var e in entries)
        {
            var parts = e.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0) continue;
            var dirs = parts.Take(parts.Count - 1).ToList();
            if (dirs.Any(d => Regex.IsMatch(d, @"(^|[^a-z])mono($|[^a-z])", RegexOptions.IgnoreCase))) continue;
            dirs.RemoveAll(d => d.Contains("il2cpp", StringComparison.OrdinalIgnoreCase));
            map[e] = string.Join("/", dirs.Append(parts[^1]));
        }
        // step through a folder every nested entry shares, unless it is one of the game's own folders
        // (loose files beside it, like a readme, do not count; a loose DLL means there is no wrapper)
        while (true)
        {
            var nested = map.Where(kv => kv.Value.Contains('/')).ToList();
            if (nested.Count == 0 || map.Values.Any(v => !v.Contains('/') && v.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) break;
            var first = nested[0].Value.Split('/')[0];
            if (GameFolders.Contains(first, StringComparer.OrdinalIgnoreCase) || nested.Any(kv => kv.Value.Split('/')[0] != first)) break;
            foreach (var kv in nested) map[kv.Key] = kv.Value[(first.Length + 1)..];
        }
        return map;
    }

    /// <summary>Removes the extra files zips put in the game folder (used by Uninstall).</summary>
    public static void RemoveExtras()
    {
        if (!File.Exists(Manifest)) return;
        string root = Path.GetFullPath(Game.Dir);
        foreach (var f in File.ReadAllLines(Manifest).Distinct())
            if (Path.GetFullPath(f).StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(f)) File.Delete(f);
        File.Delete(Manifest);
    }
}
