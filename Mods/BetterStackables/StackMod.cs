using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BetterStackables.StackMod), "Better Stackables", "0.1.0", "Bongosmod")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BetterStackables;

/// <summary>Lets every item stack (up to Limit per slot). Looks the game's item type up by name, so a game update that renames things can't stop the mod from loading.</summary>
public class StackMod : MelonMod
{
    const int Limit = 100; // very high values (999) have been reported to upset deliveries in other stack mods
    static readonly float[] Delays = { 2, 6, 15, 40 }; // items finish loading a little after the scene does

    float _timer;
    int _next = Delays.Length; // nothing pending until a scene loads
    Type _itemType;

    public override void OnSceneWasInitialized(int index, string name) { _timer = 0; _next = 0; }

    public override void OnUpdate()
    {
        if (_next >= Delays.Length) return;
        _timer += Time.unscaledDeltaTime;
        if (_timer < Delays[_next]) return;
        _next++;
        try { Apply(); }
        catch (Exception e) { LoggerInstance.Warning("Better Stackables skipped a pass: " + e.Message); }
    }

    static IEnumerable<Type> TypesOf(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }

    void Apply()
    {
        _itemType ??= AppDomain.CurrentDomain.GetAssemblies().SelectMany(TypesOf)
            .FirstOrDefault(t => t.Name == "ItemDefinition" && t.FullName.Contains("Il2Cpp")
                                 && (t.GetProperty("StackLimit") != null || t.GetField("StackLimit") != null));
        if (_itemType == null) { LoggerInstance.Warning("Item type not found, nothing changed."); return; }

        var prop = _itemType.GetProperty("StackLimit");
        var field = prop == null ? _itemType.GetField("StackLimit") : null;
        var cast = typeof(Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(_itemType);

        int changed = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(_itemType)))
        {
            var item = cast.Invoke(o, null);
            if (item == null) continue;
            int cur = Convert.ToInt32(prop != null ? prop.GetValue(item) : field.GetValue(item));
            if (cur >= Limit) continue;
            if (prop != null) prop.SetValue(item, Limit); else field.SetValue(item, Limit);
            changed++;
        }
        if (changed > 0) LoggerInstance.Msg($"Better Stackables: {changed} items can now stack to {Limit}.");
    }
}
