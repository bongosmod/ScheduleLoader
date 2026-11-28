using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using ClothingShopInterface = Il2CppScheduleOne.UI.Shop.ClothingShopInterface;
using ListingUI = Il2CppScheduleOne.UI.Shop.ListingUI;
using Cart = Il2CppScheduleOne.UI.Shop.Cart;
using CartEntry_Clothing = Il2CppScheduleOne.UI.Shop.CartEntry_Clothing;
using ShopListing = Il2CppScheduleOne.UI.Shop.ShopListing;
using ShopInterface = Il2CppScheduleOne.UI.Shop.ShopInterface;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using S1API.Internal.Abstraction;
using S1API.Items;
using S1API.Leveling;
using S1API.Lifecycle;
using S1API.Saveables;
using S1API.Shops;
using UnityEngine;
using ItemInstance = Il2CppScheduleOne.ItemFramework.ItemInstance;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;

[assembly: MelonInfo(typeof(Backpacks.BackpacksMod), "The Backpacks Mod", "0.1.4", "Bongosmod")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Backpacks;

/// <summary>One bag you can buy at Thrifty Threads once you reach its rank.</summary>
record BagTier(int Level, string Id, string Name, int Slots, float Price, Rank Rank, Color Color);

public class BackpacksMod : MelonMod
{
    internal static readonly BagTier[] Tiers =
    {
        new(1, "bongosmod_cardboard_bag", "Cardboard Bag", 3, 650, Rank.Hoodlum, new Color(0.72f, 0.54f, 0.33f)),
        new(2, "bongosmod_backpack", "Backpack", 6, 1300, Rank.Peddler, new Color(0.22f, 0.42f, 0.78f)),
        new(3, "bongosmod_duffel_bag", "Duffel Bag", 8, 2200, Rank.Hustler, new Color(0.18f, 0.36f, 0.24f)),
    };
    const int TierStep = 3; // every bag unlocks at tier III of its rank

    float _nextCheck;
    IntPtr _hookedPlayer;
    StorageMenu _menu;

    public override void OnInitializeMelon()
    {
        ClassInjector.RegisterTypeInIl2Cpp<BagSlots>(new RegisterTypeOptions { Interfaces = new[] { typeof(IItemSlotOwner) } });
        GameLifecycle.OnPreLoad += CreateItems;
        GameLifecycle.OnLoadComplete += OnLoadComplete;
        GameLifecycle.OnSaveStart += Bag.Save; // write the bag into this save before the game writes its files
        LoggerInstance.Msg("The Backpacks Mod loaded. Press B in game to open your bag.");
    }

    // ---------- the bag items, sold at Thrifty Threads ----------
    static readonly Dictionary<string, Sprite> Icons = new();

    static void CreateItems()
    {
        Bag.Reset(); // a new save is loading: forget the last one's bag until its data arrives
        foreach (var t in Tiers)
        {
            if (ItemManager.IsItemRegistered(t.Id)) continue;
            try
            {
                ItemCreator.CreateBuilder()
                    .WithBasicInfo(t.Id, t.Name,
                        $"Press B to open it. {t.Slots} extra inventory slots that police can't search. " +
                        "Everything inside is lost if you die or get arrested.", ItemCategory.Clothing)
                    .WithIcon(Icons.TryGetValue(t.Id, out var icon) ? icon : Icons[t.Id] = DrawIcon(t))
                    .WithPricing(t.Price, 0.5f)
                    .WithStackLimit(1)
                    .WithLegalStatus(LegalStatus.Legal)
                    .WithRequiredRank(new FullRank(t.Rank, TierStep))
                    .Build();
            }
            catch (Exception e) { MelonLogger.Error($"Backpacks: could not create {t.Name}: {e}"); }
        }
    }

    void OnLoadComplete()
    {
        _menu = null;
        try
        {
            var shop = ShopManager.GetAllShops().FirstOrDefault(s => s.Name != null && s.Name.Contains("Thrifty", StringComparison.OrdinalIgnoreCase));
            if (shop == null) MelonLogger.Warning("Backpacks: Thrifty Threads not found. Shops: " + string.Join(", ", ShopManager.GetAllShops().Select(s => s.Name)));
            foreach (var t in Tiers)
            {
                var def = ItemManager.GetItemDefinition(t.Id);
                if (def == null) continue;
                if (shop != null && !shop.HasItem(t.Id)) shop.AddItem(def, t.Price);
                // shows up in the "unlocked at this rank" list when ranking up
                var rank = new FullRank(t.Rank, TierStep);
                if (!LevelManager.GetUnlockables(rank).Any(u => u.Title == t.Name)) LevelManager.AddUnlockable(new Unlockable(rank, t.Name, def.Icon));
            }
        }
        catch (Exception e) { MelonLogger.Error("Backpacks: could not stock Thrifty Threads: " + e); }
        Bag.Load();
    }

    // ---------- every frame: B to open, bag purchases, death and arrest ----------
    public override void OnUpdate()
    {
        var player = Player.Local;
        if (player == null) return;

        if (player.Pointer != _hookedPlayer)
        {
            _hookedPlayer = player.Pointer;
            player.add_onArrested((Il2CppSystem.Action)new Action(() => Bag.Lose("arrested")));
            var health = player.GetComponent<Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth>();
            health?.onDie.AddListener((UnityEngine.Events.UnityAction)new Action(() => Bag.Lose("died")));
        }

        if (Time.unscaledTime >= _nextCheck) { _nextCheck = Time.unscaledTime + 0.5f; TakeBoughtBags(); }

        if (BPressed()) ToggleMenu();
    }

    static bool BPressed()
    {
        try { var k = UnityEngine.InputSystem.Keyboard.current; return k != null && k.bKey.wasPressedThisFrame; }
        catch { return false; }
    }

    // Why a bag can't go in the cart right now, or null when it can.
    internal static string CantAdd(BagTier t, Cart cart)
    {
        if (t.Level <= Bag.Level) return Bag.Level == t.Level ? $"You already carry the {t.Name}." : "Your bag is already bigger.";
        if (LevelManager.CurrentRank < new FullRank(t.Rank, TierStep)) return $"The {t.Name} unlocks at {t.Rank} III.";
        if (ThriftyCart.BagsIn(cart).Count > 0) return "You can only buy one bag at a time.";
        return null;
    }

    // Checkout with a bag in the cart: the bag is paid by card and goes straight on your back.
    internal static bool PayAndGive(BagTier t)
    {
        if (S1API.Money.Money.GetOnlineBalance() < t.Price) { Notify(t, "Not enough money", $"The {t.Name} costs ${t.Price:0}, paid by card."); return false; }
        S1API.Money.Money.CreateOnlineTransaction("Thrifty Threads: " + t.Name, -t.Price, 1, "");
        Bag.Upgrade(t.Level, t.Slots);
        Notify(t, $"{t.Name} bought", $"{t.Slots} extra slots. Press B to open it.");
        MelonLogger.Msg($"Backpacks: bought the {t.Name} for ${t.Price:0} by card.");
        return true;
    }

    internal static void Notify(BagTier t, string title, string text)
    {
        try { Singleton<NotificationsManager>.Instance?.SendNotification(title, text, Icons.TryGetValue(t.Id, out var i) ? i : null, 5f, true); }
        catch (Exception e) { MelonLogger.Msg($"Backpacks: {title}: {text} ({e.Message})"); }
    }

    // A bag that reached the inventory some other way (another mod, the console) goes on your back too.
    static void TakeBoughtBags()
    {
        var inv = PlayerSingleton<PlayerInventory>.Instance;
        if (inv == null) return;
        foreach (var t in Tiers.OrderByDescending(x => x.Level)) // biggest bag first
        {
            if (inv.GetAmountOfItem(t.Id) == 0 || t.Level <= Bag.Level) continue; // a smaller bag than yours stays in your inventory
            inv.RemoveAmountOfItem(t.Id, 1);
            Bag.Upgrade(t.Level, t.Slots);
            MelonLogger.Msg($"Backpacks: you now carry the {t.Name} ({t.Slots} slots). Press B to open it.");
            return;
        }
    }

    void ToggleMenu()
    {
        if (_menu == null) _menu = UnityEngine.Object.FindObjectOfType<StorageMenu>(true);
        if (_menu == null) return;
        if (_menu.IsOpen)
        {
            if (Bag.IsOpen) _menu.Close(); // the full close Esc does (CloseMenu only cleared the slots)
            return;
        }
        // only during normal play: not in another menu, the phone, a dialogue or while dead or arrested
        var player = Player.Local;
        if (Cursor.lockState != CursorLockMode.Locked || player.IsArrested) return;
        var tier = Tiers.FirstOrDefault(t => t.Level == Bag.Level);
        if (tier == null) return; // no bag yet
        Bag.IsOpen = true;
        _menu.Open(Bag.Owner, tier.Name, "Police can't search it. Lost if you die or get arrested.", (Il2CppSystem.Action)new Action(() => { Bag.IsOpen = false; Bag.Save(); }));
    }

    // a simple bag drawn in code: a rounded body in the bag's color with a handle and a strap
    static Sprite DrawIcon(BagTier t)
    {
        const int n = 128;
        var tex = new Texture2D(n, n) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        var dark = t.Color * 0.65f; dark.a = 1;
        float w = t.Level == 3 ? 100 : t.Level == 2 ? 74 : 84, h = t.Level == 3 ? 56 : t.Level == 2 ? 84 : 70;
        float x0 = (n - w) / 2, y0 = 14, r = t.Level == 1 ? 4 : 18;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                float cx = Mathf.Clamp(x, x0 + r, x0 + w - r), cy = Mathf.Clamp(y, y0 + r, y0 + h - r);
                bool body = (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r && x >= x0 && x <= x0 + w && y >= y0 && y <= y0 + h;
                float hx = x - n / 2f, hy = y - (y0 + h);
                float hr = Mathf.Sqrt(hx * hx + hy * hy);
                bool handle = hy >= 0 && hr >= 14 && hr <= 20;
                bool strap = body && (t.Level == 1 ? Mathf.Abs(y - (y0 + h * 0.62f)) < 4 : Mathf.Abs(x - n / 2f) < 4 && y < y0 + h * 0.55f);
                tex.SetPixel(x, y, strap || handle ? dark : body ? t.Color : Color.clear);
            }
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        s.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return s;
    }
}

/// <summary>
/// Thrifty Threads is a clothing shop: its "add to cart" and its checkout read clothing colours and crash on anything
/// else. For our bags only: the click puts the bag in the cart directly, and at checkout the bag is taken out of the
/// cart, paid by card and put on your back, before the game checks out the rest of the cart as usual.
/// </summary>
static class ThriftyCart
{
    internal static List<(ShopListing Listing, BagTier Tier)> BagsIn(Cart cart)
    {
        var found = new List<(ShopListing, BagTier)>();
        if (cart?.cartDictionary == null) return found;
        foreach (var kv in cart.cartDictionary)
        {
            var tier = BackpacksMod.Tiers.FirstOrDefault(t => t.Id == kv.Key?.Item?.ID);
            if (tier != null) found.Add((kv.Key, tier));
        }
        return found;
    }

    static float BagTotal(Cart cart) => BagsIn(cart).Sum(b => b.Tier.Price);

    [HarmonyLib.HarmonyPatch(typeof(ClothingShopInterface), nameof(ClothingShopInterface.AddItem))]
    static class AddToCart
    {
        static bool Prefix(ClothingShopInterface __instance, ListingUI __0)
        {
            var listing = __0?.Listing;
            var tier = BackpacksMod.Tiers.FirstOrDefault(t => t.Id == listing?.Item?.ID);
            if (tier == null) return true; // real clothing: the game's own code
            try
            {
                var why = __instance.Cart.GetCartCount(listing) > 0 ? $"The {tier.Name} is already in your cart." : BackpacksMod.CantAdd(tier, __instance.Cart);
                if (why != null) BackpacksMod.Notify(tier, "Can't add to cart", why);
                else __instance.Cart.AddItem(listing, 1);
            }
            catch (Exception e) { MelonLogger.Error($"Backpacks: adding the {tier.Name} to the cart failed: {e}"); }
            return false;
        }
    }

    // the clothing cart line writes "1x Name (colour)" and crashes without a colour: bags get a plain title
    [HarmonyLib.HarmonyPatch(typeof(CartEntry_Clothing), nameof(CartEntry_Clothing.UpdateTitle))]
    static class CartTitle
    {
        static bool Prefix(CartEntry_Clothing __instance)
        {
            var tier = BackpacksMod.Tiers.FirstOrDefault(t => t.Id == __instance.Listing?.Item?.ID);
            if (tier == null) return true;
            if (__instance.NameLabel != null) __instance.NameLabel.text = $"{__instance.Quantity}x {tier.Name}";
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Cart), nameof(Cart.Buy))]
    static class Checkout
    {
        static bool Prefix(Cart __instance)
        {
            var bags = BagsIn(__instance);
            if (bags.Count == 0) return true;
            try
            {
                var (listing, tier) = bags[0]; // only one bag fits in the cart at a time
                if (!BackpacksMod.PayAndGive(tier)) return false; // not enough on the card: nothing is bought
                __instance.RemoveListing(listing);
            }
            catch (Exception e) { MelonLogger.Error("Backpacks: checkout failed: " + e); return false; }
            return __instance.cartDictionary.Count > 0; // clothes left in the cart: the game checks them out as usual
        }
    }

    // the bag is paid by card: the rest of the cart must be affordable the shop's usual way
    [HarmonyLib.HarmonyPatch(typeof(Cart), nameof(Cart.CanPlayerAffordCart))]
    static class Afford
    {
        static void Postfix(Cart __instance, ref bool __result)
        {
            float bags = BagTotal(__instance);
            if (bags <= 0) return;
            float rest = __instance.GetPriceSum() - bags, cash = S1API.Money.Money.GetCashBalance(), card = S1API.Money.Money.GetOnlineBalance();
            __result = __instance.Shop.PaymentType switch
            {
                ShopInterface.EPaymentType.Cash => cash >= rest && card >= bags,
                ShopInterface.EPaymentType.Online => card >= rest + bags,
                _ => card >= bags && cash + card >= rest + bags,
            };
        }
    }

    // a bag goes on your back, not in your inventory: it needs no free slot
    [HarmonyLib.HarmonyPatch(typeof(Cart), nameof(Cart.GetTotalSlotRequirement))]
    static class Slots
    {
        static void Postfix(Cart __instance, ref int __result) => __result = Math.Max(0, __result - BagsIn(__instance).Count);
    }
}

/// <summary>The bag you carry: its size and its slots, shown with the game's own storage screen.</summary>
static class Bag
{
    public static int Level { get; private set; } // 0 = no bag yet
    public static bool IsOpen;
    static BagSlots _slots;

    public static IItemSlotOwner Owner => new(Slots.Pointer);
    static BagSlots Slots => _slots ??= new BagSlots();

    public static void Reset()
    {
        Level = 0;
        IsOpen = false;
        _slots = null;
    }

    public static void Upgrade(int level, int slotCount)
    {
        Level = level;
        Slots.Resize(slotCount);
        Save();
    }

    // death or arrest: everything inside is gone
    public static void Lose(string why)
    {
        if (Level == 0 || _slots == null) return;
        int lost = _slots.Clear();
        if (lost > 0) MelonLogger.Msg($"Backpacks: you {why}, and lost the {lost} item(s) in your bag.");
        Save();
    }

    public static void Save()
    {
        var data = BagSave.Instance?.Data;
        if (data == null) return;
        data.Level = Level;
        data.Items = _slots?.Snapshot() ?? new List<string>();
    }

    public static void Load()
    {
        var data = BagSave.Instance?.Data;
        if (data == null || data.Level <= 0) return;
        Level = data.Level;
        Slots.Resize(BackpacksMod.Tiers.First(t => t.Level == Math.Min(data.Level, 3)).Slots);
        Slots.Restore(data.Items);
    }
}

/// <summary>What is saved in each save file (by S1API, next to the game's own save data).</summary>
public class BagData
{
    public int Level;
    public List<string> Items = new(); // one entry per slot: the game's own item save text, or "" when empty
}

public class BagSave : Saveable
{
    public static BagSave Instance { get; private set; }
    [SaveableField("backpacks")] public BagData Data = new();

    public BagSave() { Instance = this; }

    protected override void OnLoaded() => Instance = this;
}

/// <summary>
/// The bag's item slots, as a game "slot owner" so the game's storage screen can show and edit them.
/// Unlike chests it lives only on this player's PC: nothing is sent over the network.
/// </summary>
public class BagSlots : Il2CppSystem.Object
{
    public BagSlots(IntPtr ptr) : base(ptr) { }
    public BagSlots() : base(ClassInjector.DerivedConstructorPointer<BagSlots>())
    {
        ClassInjector.DerivedConstructorBody(this);
        ItemSlots = new SlotList();
    }

    public SlotList ItemSlots { get; set; }

    public void Resize(int count)
    {
        var owner = new IItemSlotOwner(Pointer);
        while (ItemSlots.Count < count)
        {
            var slot = new ItemSlot();
            slot.SetSlotOwner(owner);
            if (!ItemSlots.Contains(slot)) ItemSlots.Add(slot); // SetSlotOwner may already add it
        }
    }

    public int Clear()
    {
        int n = 0;
        foreach (var s in ItemSlots) if (s.ItemInstance != null) { s.ClearStoredInstance(true); n++; }
        return n;
    }

    [HideFromIl2Cpp] // used only by this mod: the game never needs to see it
    public List<string> Snapshot()
    {
        var list = new List<string>();
        foreach (var s in ItemSlots) list.Add(s.ItemInstance != null ? s.ItemInstance.GetItemData().GetJson(false) : "");
        return list;
    }

    [HideFromIl2Cpp]
    public void Restore(List<string> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (string.IsNullOrEmpty(items[i])) continue;
            try
            {
                var item = ItemDeserializer.LoadItem(items[i]);
                if (item == null) continue;
                if (i < ItemSlots.Count) { ItemSlots[i].SetStoredItem(item, true); continue; }
                // saved when bags had more slots: hand it back instead of losing it
                var inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv != null && inv.CanItemFitInInventory(item, item.Quantity)) inv.AddItemToInventory(item);
                else MelonLogger.Warning($"Backpacks: no room for an item from slot {i + 1} (your bag got smaller in an update), and no inventory space for it.");
            }
            catch (Exception e) { MelonLogger.Warning($"Backpacks: could not load an item in slot {i + 1}: {e.Message}"); }
        }
    }

    // ---------- what the game calls on a slot owner (all local: the bag is never networked) ----------
    public void SetStoredInstance(NetworkConnection conn, int itemSlotIndex, ItemInstance instance)
    {
        if (itemSlotIndex < 0 || itemSlotIndex >= ItemSlots.Count) return;
        if (instance == null) ItemSlots[itemSlotIndex].ClearStoredInstance(true);
        else ItemSlots[itemSlotIndex].SetStoredItem(instance, true);
    }

    public void SetItemSlotQuantity(int itemSlotIndex, int quantity)
    {
        if (itemSlotIndex >= 0 && itemSlotIndex < ItemSlots.Count) ItemSlots[itemSlotIndex].SetQuantity(quantity, true);
    }

    public void SetSlotLocked(NetworkConnection conn, int itemSlotIndex, bool locked, NetworkObject lockOwner, string lockReason)
    {
        if (itemSlotIndex < 0 || itemSlotIndex >= ItemSlots.Count) return;
        if (locked) ItemSlots[itemSlotIndex].ApplyLock(lockOwner, lockReason, true);
        else ItemSlots[itemSlotIndex].RemoveLock(true);
    }

    public void SetSlotFilter(NetworkConnection conn, int itemSlotIndex, SlotFilter filter)
    {
        if (itemSlotIndex >= 0 && itemSlotIndex < ItemSlots.Count) ItemSlots[itemSlotIndex].SetPlayerFilter(filter, true);
    }

    public void SendItemSlotDataToClient(NetworkConnection conn) { }

    public int GetQuantitySum() { int n = 0; foreach (var s in ItemSlots) n += s.Quantity; return n; }

    public int GetQuantityOfItem(string id)
    {
        int n = 0;
        foreach (var s in ItemSlots) if (s.ItemInstance != null && s.ItemInstance.ID == id) n += s.Quantity;
        return n;
    }

    public int GetNonEmptySlotCount() { int n = 0; foreach (var s in ItemSlots) if (s.ItemInstance != null) n++; return n; }

    public ItemSlot GetFirstSlotContaining(string id)
    {
        foreach (var s in ItemSlots) if (s.ItemInstance != null && s.ItemInstance.ID == id) return s;
        return null;
    }
}
