using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using NipplePiercings.Logic;

namespace NipplePiercings
{
    /// <summary>
    /// Nipple piercings: studs, barbells, rings and pendants on the nipples (the game's own piercing anchors), hanging pieces
    /// swinging with her (<see cref="PendantChain"/>), optionally a chain between the two (<see cref="SaggingChain"/>). Not a
    /// restraint - it holds nothing and blocks nothing - so it does not use RestraintHub.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.NipplePiercings";
        public const string PluginName = "NipplePiercings";
        public const string PluginVersion = "1.0.1";
        internal const string ModelResource = "NipplePiercings.assets.piercings.npd";
        public const string None = "none";

        internal static ManualLogSource Logger;
        // public: tunable live with the bridge (game set)
        public static ConfigEntry<bool> Enabled, Chain, ArmCollision, FloorCollision;
        public static ConfigEntry<string> Left, Right;
        public static ConfigEntry<float> Size, NippleFit, ForwardCm, DepthCm, Damping, Gravity, BreastSize, ChainSlack, Volume;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            PiercingSet set;
            try
            {
                using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(ModelResource)
                                 ?? throw new FileNotFoundException("embedded resource " + ModelResource + " missing");
                set = PiercingSet.Read(s);
            }
            catch (Exception e)
            {
                Logger.LogError("piercing models could not be read, the mod stays off: " + e.Message);
                return;
            }
            var choices = new List<string> { None };
            choices.AddRange(set.Wearable());
            string list = string.Join(", ", choices);

            Enabled = Config.Bind("General", "Enabled", true, "Wear the piercings (applies live).");
            Left = Config.Bind("Piercing", "Left", None, new ConfigDescription(
                "Piercing on her left nipple (" + list + "; applies live). Equipping a piercing in the game's costume menu " +
                "(Inventory > Costume > General (Upper Body)) sets both sides.", new AcceptableValueList<string>(choices.ToArray())));
            Right = Config.Bind("Piercing", "Right", None, new ConfigDescription(
                "Piercing on her right nipple (" + list + "; applies live). Equipping a piercing in the game's costume menu " +
                "(Inventory > Costume > General (Upper Body)) sets both sides.", new AcceptableValueList<string>(choices.ToArray())));
            Chain = Config.Bind("Piercing", "Chain", false,
                "A chain hanging between the two piercings (needs one on each side; applies live; the costume menu's \"Nipple Chain\").");
            Size = Config.Bind("Model", "Size", 1f, new ConfigDescription(
                "Size of all piercings (1 = the crystal pendant 6.5 cm long at height 1, scaled with her height setting; applies live).",
                new AcceptableValueRange<float>(0.3f, 3f)));
            NippleFit = Config.Bind("Model", "NippleFit", 1.7f, new ConfigDescription(
                "Size of barbells and rings (they go through the nipple) relative to her nipple: they follow her nipple size setting " +
                "and height (applies live).", new AcceptableValueRange<float>(0.3f, 4f)));
            ForwardCm = Config.Bind("Model", "ForwardCm", 0f, new ConfigDescription(
                "Moves the piercings out of (+) or into (-) the nipple (cm; at 0 a stud's ball is half pressed into the tip; applies live).",
                new AcceptableValueRange<float>(-2f, 2f)));
            DepthCm = Config.Bind("Model", "DepthCm", 0.45f, new ConfigDescription(
                "How far behind the nipple's tip barbells and rings go through it (cm for the first save's nipples at height 1; scales with " +
                "the nipple; applies live).", new AcceptableValueRange<float>(0f, 2f)));
            Damping = Config.Bind("Physics", "Damping", 1.5f, new ConfigDescription(
                "How quickly a swing dies down (share of the speed lost per second; 0 = swings on and on).", new AcceptableValueRange<float>(0f, 20f)));
            Gravity = Config.Bind("Physics", "Gravity", 1f, new ConfigDescription(
                "Gravity on the hanging pieces relative to the real one (lower = floatier).", new AcceptableValueRange<float>(0f, 3f)));
            BreastSize = Config.Bind("Physics", "BreastSize", 0.5f, new ConfigDescription(
                "Radius of the spheres that keep the pieces out of her breasts, as a share of the distance from the breast's root bone " +
                "to the nipple (0 = no breast collision).", new AcceptableValueRange<float>(0f, 1f)));
            ArmCollision = Config.Bind("Physics", "ArmCollision", true, "Hanging pieces stay out of her arms.");
            FloorCollision = Config.Bind("Physics", "FloorCollision", true, "Hanging pieces stay above the floor under her.");
            ChainSlack = Config.Bind("Physics", "ChainSlack", 1.3f, new ConfigDescription(
                "Length of the chain between the nipples relative to their distance when it is put on (1 = taut).", new AcceptableValueRange<float>(1f, 3f)));
            Volume = Config.Bind("Sound", "Volume", 0.4f, new ConfigDescription(
                "Loudness of the jingle when a hanging piercing swings hard (0 = silent; follows the game's sound effect volume).",
                new AcceptableValueRange<float>(0f, 1f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0,
                "Bit mask for tests (live): 1 = no physics (hang as modelled, in the nipple's frame), 2 = no collisions, 4 = no sound, 8 = no sparkle.");

            PiercingController.Set = set;
            try { Closet.Apply(new Harmony(PluginGuid)); }
            catch (Exception e) { Logger.LogError("costume menu hooks failed, the piercings are chosen in the config only: " + e); }
            ClassInjector.RegisterTypeInIl2Cpp<PiercingController>();
            AddComponent<PiercingController>();
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready (" + choices.Count + " choices; left " + Left.Value +
                           ", right " + Right.Value + ", chain " + Chain.Value + ")");
        }
    }
}
