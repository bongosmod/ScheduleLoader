using MelonLoader;
using ScheduleMods.Utils;

[assembly: MelonInfo(typeof(UtilsMod), "ScheduleMods Utils", "0.1.0", "Bongosmod")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ScheduleMods.Utils;

public class UtilsMod : MelonMod
{
    public override void OnInitializeMelon() => ModTools.Log($"Utils ready. Mods installed: {ModTools.GetMods().Count}");
}
