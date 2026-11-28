using MelonLoader;
using MelonLoader.Utils;

namespace ScheduleMods.Utils;

public record ModEntry(string Name, bool Enabled, bool Required);

/// <summary>Helpers shared by ScheduleMods: list mods, enable/disable them, log.</summary>
public static class ModTools
{
    static string Dir => MelonEnvironment.ModsDirectory;

    public static List<ModEntry> GetMods() => Directory.GetFiles(Dir)
        .Select(f => Path.GetFileName(f))
        .Where(n => n.EndsWith(".dll") || n.EndsWith(".dll.disabled"))
        .Select(n => new ModEntry(n[..n.IndexOf(".dll")], n.EndsWith(".dll"), n.StartsWith("ScheduleMods.Utils")))
        .ToList();

    // Takes effect next launch.
    public static void SetEnabled(string name, bool on)
    {
        if (name == "ScheduleMods.Utils") return;
        var dll = Path.Combine(Dir, name + ".dll");
        if (on && File.Exists(dll + ".disabled")) File.Move(dll + ".disabled", dll);
        else if (!on && File.Exists(dll)) File.Move(dll, dll + ".disabled");
    }

    public static void Log(string msg) => MelonLogger.Msg("[ScheduleMods] " + msg);
}
