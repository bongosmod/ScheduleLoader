using System.IO.Compression;
using System.Reflection;

namespace ScheduleLoader;

/// <summary>The exe contains the mod source and thumbnails; this unpacks them into the app data folder when the exe is new or changed.</summary>
static class Payload
{
    public static void Extract()
    {
        var asm = Assembly.GetExecutingAssembly();
        string stampFile = Path.Combine(Paths.App, "payload.stamp");
        string stamp = asm.ManifestModule.ModuleVersionId.ToString();
        if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp && Directory.Exists(Path.Combine(Paths.App, "src"))) return;

        using var s = asm.GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("This copy of Schedule Loader is missing its built-in files. Please download it again.");
        using var zip = new ZipArchive(s);
        foreach (var d in new[] { "src", "Assets" })
        {
            var path = Path.Combine(Paths.App, d);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        zip.ExtractToDirectory(Paths.App, true);
        File.WriteAllText(stampFile, stamp);
    }
}
