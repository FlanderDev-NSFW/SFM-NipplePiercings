using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Master.Cosplay;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.Scripts.Costume;
using ExposureUnnoticed2.ObjectUI.InventoryPanel;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NipplePiercings
{
    /// <summary>
    /// The piercings as items of the game's own costume menu (Inventory > Costume, the "General" upper body series next to the
    /// game's nipple piercing): one item per piercing (equipping it puts it on both nipples, equipping another swaps it) and one
    /// for the chain between them.
    /// <para>
    /// The items are new records appended to the series (<c>MCosplay</c>, <c>GeneralUpperBody</c>), plain
    /// <see cref="RCosplayParts"/> with the game's nipple piercing's settings (a copy of its own record type would switch on the
    /// game's piercing). Their state never reaches the save: owned / equipped / equip are answered for these ids by the hooks
    /// below from the mod's config, and the game's own equip of a body object is skipped for them (the game has none) - a save
    /// written with them loads without the mod. The game's equipped table (<c>PlayerCosplayPartsDict</c>) mirrors the config
    /// (<see cref="Sync"/>) because the inventory reads it directly: the "Equipped" page and the slot count. Ids are the next free ones in the series (33.. today); the mod keys its state by
    /// the piercing's name, so a game update adding records only moves them.
    /// </para>
    /// </summary>
    internal static class Closet
    {
        private const CosplayType Series = CosplayType.GeneralUpperBody;
        private const string KeyPrefix = "m_nipplepiercings_";
        public const string ChainItem = "chain";

        private static readonly Dictionary<int, string> _byUid = new Dictionary<int, string>();  // unique id -> piercing id
        private static readonly Dictionary<int, RCosplayParts> _records = new Dictionary<int, RCosplayParts>();
        private static readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private static int _firstId = -1;
        public static string Status = "not added"; // diagnostics

        private static readonly Dictionary<string, (string Name, string Description)> Labels = new Dictionary<string, (string, string)>
        {
            ["stud"] = ("Nipple Studs", "A small silver ball on each nipple."),
            ["crystal"] = ("Crystal Nipple Pendants", "A turquoise crystal on a short chain, swinging from each nipple."),
            ["heart"] = ("Heart Nipple Pendants", "A curved barbell with a chain and a ruby heart."),
            ["barbell"] = ("Nipple Barbells", "Straight barbells through the nipples."),
            ["curved"] = ("Curved Nipple Barbells", "Curved barbells through the nipples, top to bottom."),
            ["curved_small"] = ("Small Curved Nipple Barbells", "Small curved barbells through the nipples."),
            ["ring"] = ("Nipple Rings", "Captive bead rings through the nipples."),
            ["ring_small"] = ("Small Nipple Rings", "Small rings through the nipples."),
            ["hoop"] = ("Big Nipple Hoops", "Large hoops hanging from the nipples."),
            ["chain_ring"] = ("Nipple Chain Rings", "Plain rings through the nipples."),
            ["chain_ring_big"] = ("Big Nipple Chain Rings", "Large plain rings through the nipples."),
            [ChainItem] = ("Nipple Chain", "A chain hanging between the nipple piercings (needs piercings)."),
        };

        public static void Apply(Harmony harmony)
        {
            var me = typeof(Closet);
            void Prefix(System.Reflection.MethodBase target, string name) => harmony.Patch(target, prefix: new HarmonyMethod(me, name));
            // The game finds a record by its unique id in a table built at start (the menu's equip looked the items up there
            // and stopped without them, observed 2026-10-25).
            Prefix(AccessTools.Method(typeof(MCosplay), nameof(MCosplay.GetParts)), nameof(GetPartsPrefix));
            Prefix(AccessTools.Method(typeof(GameStateFacade), nameof(GameStateFacade.IsBringCosplayParts)), nameof(IsBringPrefix));
            Prefix(AccessTools.Method(typeof(GameStateFacade), nameof(GameStateFacade.IsEquipCosplayParts)), nameof(IsEquipPrefix));
            Prefix(AccessTools.Method(typeof(GameStateFacade), nameof(GameStateFacade.SetActiveCosplayParts)), nameof(SetActivePrefix));
            Prefix(AccessTools.Method(typeof(GameStateFacade), nameof(GameStateFacade.GetCosplayPartsOptionData)), nameof(OptionDataPrefix));
            Prefix(AccessTools.Method(typeof(PlayerCosplayManager), nameof(PlayerCosplayManager.SetCosplayActive)), nameof(SetCosplayActivePrefix));
            // The game also switches every record's body object on / off itself (loading, taking everything off): ids past the
            // slot's own objects are skipped (NullReferenceException in SetActiveParts for each of these items, observed 2026-10-25).
            // So are empty slots: Cosplay Loader's own SetActiveParts prefix grows the array to the id with nulls when it has no
            // model for the record, and runs before this one (the same exception again, observed 2026-11-02).
            Prefix(AccessTools.Method(typeof(CosplayController), nameof(CosplayController.SetActiveParts)), nameof(SetActivePartsPrefix));
            Prefix(AccessTools.Method(typeof(CosplayController), nameof(CosplayController.IsActiveParts)), nameof(IsActivePartsPrefix));
            // The inventory's "Equipped" page and its slot count read the equipped table itself, not IsEquipCosplayParts (the items
            // were missing there, user report 2026-11-02): keep it in step with the config before the game reads it.
            Prefix(AccessTools.Method(typeof(InventoryPanelCosplayView), nameof(InventoryPanelCosplayView.ChangeSeries)), nameof(SyncPrefix));
            Prefix(AccessTools.Method(typeof(InventoryPanelView), nameof(InventoryPanelView.CalcEquipCount)), nameof(SyncPrefix));
            harmony.Patch(AccessTools.Method(typeof(PlayerCosplayManager), nameof(PlayerCosplayManager.SetupCostumeFromGameState)),
                          postfix: new HarmonyMethod(me, nameof(SyncPrefix)));
            Plugin.Left.SettingChanged += (_, _) => Sync();
            Plugin.Right.SettingChanged += (_, _) => Sync();
            Plugin.Chain.SettingChanged += (_, _) => Sync();
            harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)),
                          prefix: new HarmonyMethod(me, nameof(SavePrefix)), postfix: new HarmonyMethod(me, nameof(SavePostfix)));
            harmony.Patch(AccessTools.Method(typeof(Localizer), nameof(Localizer.Trans), new[] { typeof(string) }),
                          postfix: new HarmonyMethod(me, nameof(TransPostfix)));
        }

        /// <summary>Append the items to the series once the game's master data is loaded; true once they are there.</summary>
        public static bool TryAdd(IReadOnlyList<string> piercings)
        {
            if (_firstId >= 0) return true;
            var series = MCosplay.dict;
            if (series == null || !series.ContainsKey(Series)) return false;
            RCosplay record = series[Series];
            var parts = record.PartsRecords;
            if (parts == null || parts.Length == 0) return false;
            RCosplayParts template = null;
            foreach (var p in parts) if (p != null && p.NameKey == "m_cosplay_general_upper_nip_piasu") template = p;
            var ids = new List<string>(piercings) { ChainItem };
            var grown = new Il2CppReferenceArray<RCosplayParts>(parts.Length + ids.Count);
            for (int i = 0; i < parts.Length; i++) grown[i] = parts[i];
            _firstId = parts.Length;
            for (int i = 0; i < ids.Count; i++)
            {
                var item = ScriptableObject.CreateInstance<RCosplayParts>();
                item.name = "NipplePiercings." + ids[i];
                item.hideFlags = HideFlags.DontUnloadUnusedAsset;
                item.Id = _firstId + i;
                item.ParentCosplayType = Series;
                item.CostumeCategory = template != null ? template.CostumeCategory : ExposureUnnoticed2.Master.Costume.CostumeCategory.Default;
                item.NameKey = KeyPrefix + ids[i];
                item.DescriptionKey = KeyPrefix + ids[i] + "_desc";
                item.IsNotSell = true;
                item.Price = 0;
                item.ReleaseRank = 0;
                item.CanChangeColor = false;
                item.IsShowCustomizeButton = false;
                if (template != null) item.MinBreastSize = template.MinBreastSize;
                grown[_firstId + i] = item;
                _byUid[item.UniqueId] = ids[i];
                _records[item.UniqueId] = item;
                var label = Labels.TryGetValue(ids[i], out var l) ? l : (ids[i], ids[i]);
                _names[item.NameKey] = label.Item1;
                _names[item.DescriptionKey] = label.Item2;
            }
            record.PartsRecords = grown;
            Status = ids.Count + " items, ids " + _firstId + ".." + (_firstId + ids.Count - 1) + " (unique " + grown[_firstId].UniqueId + "..)";
            Plugin.Logger.LogInfo("[SFM-ACCEPT] closet: " + Status + (template == null ? "; the game's nipple piercing record was not found" : ""));
            return true;
        }

        private static bool Ours(int uid, out string id) => _byUid.TryGetValue(uid, out id);

        /// <summary>Write each item's worn state (from the config) into the game's equipped table, as the game keeps it for its own
        /// records (uniqueId -> equipped). Taken out of the save like the other tables (<see cref="SavePrefix"/>).</summary>
        public static void Sync()
        {
            try
            {
                var dict = GameState.GameStateData?.PlayerCosplayPartsDict;
                if (dict == null) return;
                foreach (var kv in _byUid) dict[kv.Key] = IsWorn(kv.Value);
            }
            catch (Exception e) { PiercingController.Fail("closet: equipped table", e); }
        }

        private static void SyncPrefix() => Sync();

        /// <summary>Whether a piercing (or the chain) is worn, by the config.</summary>
        public static bool IsWorn(string id) =>
            id == ChainItem ? Plugin.Chain.Value : Plugin.Left.Value == id && Plugin.Right.Value == id;

        // --- hooks (each fails open: on an exception the game's own code runs) -------------------------------------------------

        private static bool GetPartsPrefix(int uniqueId, ref RCosplayParts __result)
        {
            if (!_records.TryGetValue(uniqueId, out var record)) return true;
            __result = record;
            return false;
        }

        private static bool IsBringPrefix(int uniqueId, ref bool __result)
        {
            if (!Ours(uniqueId, out _)) return true;
            __result = true; // always in her wardrobe, never bought, never saved
            return false;
        }

        private static bool IsEquipPrefix(int uniqueId, ref bool __result)
        {
            try
            {
                if (!Ours(uniqueId, out string id)) return true;
                __result = IsWorn(id);
                return false;
            }
            catch (Exception e) { PiercingController.Fail("closet: equipped check", e); return true; }
        }

        private static bool SetActivePrefix(int uniqueId, bool isActive)
        {
            try
            {
                if (!Ours(uniqueId, out string id)) return true;
                if (id == ChainItem) Plugin.Chain.Value = isActive;
                else if (isActive) { Plugin.Left.Value = id; Plugin.Right.Value = id; }
                else
                {
                    if (Plugin.Left.Value == id) Plugin.Left.Value = Plugin.None;
                    if (Plugin.Right.Value == id) Plugin.Right.Value = Plugin.None;
                }
                Sync();
                Plugin.Logger.LogInfo("[SFM-ACCEPT] closet: " + id + (isActive ? " equipped" : " taken off") + " (left " + Plugin.Left.Value +
                                      ", right " + Plugin.Right.Value + ", chain " + Plugin.Chain.Value + ")");
                return false; // the game's own equip is skipped; the table is written by Sync (and kept out of the save)
            }
            catch (Exception e) { PiercingController.Fail("closet: equip", e); return true; }
        }

        private static bool OptionDataPrefix(int uniqueId, ref CosplayPartsOptionData __result)
        {
            if (!Ours(uniqueId, out _)) return true;
            __result = new CosplayPartsOptionData(uniqueId);
            return false;
        }

        /// <summary>The equip motion ends here (not in GameStateFacade.SetActiveCosplayParts, observed 2026-10-25): for these
        /// items, apply the piercing instead of the game's body object (it has none).</summary>
        private static bool SetCosplayActivePrefix(CosplayType type, int id, bool isActive)
        {
            if (!(type == Series && _firstId >= 0 && id >= _firstId)) return true;
            foreach (var kv in _records)
                if (kv.Value.Id == id) SetActivePrefix(kv.Key, isActive);
            return false;
        }

        private static bool Beyond(CosplayController controller, int id)
        {
            var parts = controller == null ? null : controller.parts;
            return parts == null || id < 0 || id >= parts.Length || parts[id] == null;
        }

        private static bool SetActivePartsPrefix(CosplayController __instance, int id) => !Beyond(__instance, id);

        private static bool IsActivePartsPrefix(CosplayController __instance, int id, ref bool __result)
        {
            if (!Beyond(__instance, id)) return true;
            __result = false;
            return false;
        }

        // --- the save never holds these items -------------------------------------------------------------------------------
        // The game adds default entries for every record to its enhancement tables (CosplayPartsReinforceEffectDict,
        // CosplayReinforceLockState: 207 -> 219 entries with the 12 items, observed 2026-10-25). They are taken out just before
        // the game writes the save and put back after it, so a save written with the mod loads without it.

        private static readonly List<(int, Il2CppStructArray<int>)> _effects = new List<(int, Il2CppStructArray<int>)>();
        private static readonly List<(int, Il2CppStructArray<bool>)> _locks = new List<(int, Il2CppStructArray<bool>)>();
        private static readonly List<(int, bool)> _owned = new List<(int, bool)>();
        private static readonly List<(int, CosplayPartsOptionData)> _options = new List<(int, CosplayPartsOptionData)>();
        public static int Stripped; // diagnostics: entries taken out at the last save

        private static void SavePrefix()
        {
            try
            {
                _effects.Clear(); _locks.Clear(); _owned.Clear(); _options.Clear();
                var data = GameState.GameStateData;
                if (data == null || _byUid.Count == 0) return;
                foreach (int uid in _byUid.Keys)
                {
                    if (data.CosplayPartsReinforceEffectDict != null && data.CosplayPartsReinforceEffectDict.ContainsKey(uid))
                    {
                        _effects.Add((uid, data.CosplayPartsReinforceEffectDict[uid]));
                        data.CosplayPartsReinforceEffectDict.Remove(uid);
                    }
                    if (data.CosplayReinforceLockState != null && data.CosplayReinforceLockState.ContainsKey(uid))
                    {
                        _locks.Add((uid, data.CosplayReinforceLockState[uid]));
                        data.CosplayReinforceLockState.Remove(uid);
                    }
                    if (data.PlayerCosplayPartsDict != null && data.PlayerCosplayPartsDict.ContainsKey(uid))
                    {
                        _owned.Add((uid, data.PlayerCosplayPartsDict[uid]));
                        data.PlayerCosplayPartsDict.Remove(uid);
                    }
                    if (data.CosplayPartsOptionDataDict != null && data.CosplayPartsOptionDataDict.ContainsKey(uid))
                    {
                        _options.Add((uid, data.CosplayPartsOptionDataDict[uid]));
                        data.CosplayPartsOptionDataDict.Remove(uid);
                    }
                }
                Stripped = _effects.Count + _locks.Count + _owned.Count + _options.Count;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: " + Stripped + " entries of the piercing items kept out of the save");
            }
            catch (Exception e) { PiercingController.Fail("keeping the items out of the save", e); }
        }

        private static void SavePostfix()
        {
            try
            {
                var data = GameState.GameStateData;
                if (data == null) return;
                foreach (var (uid, v) in _effects) data.CosplayPartsReinforceEffectDict[uid] = v;
                foreach (var (uid, v) in _locks) data.CosplayReinforceLockState[uid] = v;
                foreach (var (uid, v) in _owned) data.PlayerCosplayPartsDict[uid] = v;
                foreach (var (uid, v) in _options) data.CosplayPartsOptionDataDict[uid] = v;
            }
            catch (Exception e) { PiercingController.Fail("restoring the items after saving", e); }
        }

        private static void TransPostfix(string key, ref string __result)
        {
            if (key != null && key.StartsWith(KeyPrefix, StringComparison.Ordinal) && _names.TryGetValue(key, out string text)) __result = text;
        }
    }
}
