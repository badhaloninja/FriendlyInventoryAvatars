using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using FrooxEngine.CommonAvatar;
using System.Linq;
using System;

namespace FriendlyInventoryAvatars
{
    public class FriendlyInventoryAvatars : ResoniteMod
    {
        public override string Name => "FriendlyInventoryAvatars";
        public override string Author => "badhaloninja";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/badhaloninja/FriendlyInventoryAvatars";

        internal static Type nestedStruct;
        internal static MethodInfo equipAvatarDelegate;
        internal static MethodInfo transpiler;

        static ModConfiguration config;
        static readonly Harmony harmony = new("ninja.badhalo.FriendlyInventoryAvatars");

        [AutoRegisterConfigKey]
        static readonly ModConfigurationKey<bool> enabled = new("enabled", "Enabled", () => true);

        public override void OnEngineInit()
        {
            config = GetConfiguration();
            config.OnThisConfigurationChanged += ConfigUpdated;
            
            // Nested delegate patches
            nestedStruct = typeof(InventoryBrowser).GetNestedType("<>c__DisplayClass101_1", BindingFlags.Instance | BindingFlags.NonPublic);
            equipAvatarDelegate = nestedStruct?.GetMethod("<OnEquipAvatar>b__3", BindingFlags.Instance | BindingFlags.NonPublic);

            transpiler = AccessTools.Method(typeof(Patch), nameof(Patch.Transpile));

            if (config.GetValue(enabled)) {
                if (equipAvatarDelegate != null && transpiler != null)
                {
                    harmony.Patch(equipAvatarDelegate, transpiler: new(transpiler));
                } else
                {
                    Error("Unable to patch, method is null");
                    Msg($"Nested struct is: {(nestedStruct != null ? nestedStruct.Name : "null")}");
                    Msg($"Target method is: {(equipAvatarDelegate != null ? equipAvatarDelegate.Name : "null")}");
                    config.Set(enabled, false, "ERROR_DISABLE");
                }
            }
        }

        private void ConfigUpdated(ConfigurationChangedEvent configurationChangedEvent)
        {
            if(configurationChangedEvent.Key == enabled && configurationChangedEvent.Label != "ERROR_DISABLE")
            {
                if(config.GetValue(enabled))
                {
                    if (equipAvatarDelegate != null && transpiler != null)
                    {
                        harmony.Patch(equipAvatarDelegate, transpiler: new(transpiler));
                    }
                    else
                    {
                        // Log and disable
                        Error("Unable to patch, method is null");
                        Msg($"Nested struct is: {(nestedStruct != null ? nestedStruct.Name : "null")}");
                        Msg($"Target method is: {(equipAvatarDelegate != null ? equipAvatarDelegate.Name : "null")}");
                        config.Set(enabled, false, "ERROR_DISABLE");
                    }
                } else
                {
                    if (equipAvatarDelegate != null && transpiler != null)
                    {
                        harmony.Unpatch(equipAvatarDelegate, transpiler);
                    }
                }
            }
        }

        class Patch
        {
            static readonly MethodInfo ClearEquipped = AccessTools.Method(typeof(AvatarManager), "ClearEquipped");
            static readonly MethodInfo Equip = AccessTools.Method(typeof(AvatarManager), "Equip");
            public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
            {
                // The <OnEquipAvatar>b__3 delegate ended up being fairly small so this is straight forward
                // First we just remove the ClearEquipped call to stop it from deleting everything that's equipped
                // Then we tell the Equip call to forceDestroyOld which will only destroy the conflicting avatars

                var list = instructions.ToList();
                
                for(var i = 0; i < list.Count; i++)
                {
                    var instruction = list[i];

                    if (instruction.Calls(ClearEquipped))
                    {   // Lazily cancel out the dup instruction while getting rid of the ClearEquipped call at the same time
                        // The AvatarManager instance reference is duplicated in the stack before being called by ClearEquipped so it can also be used by the Equip call
                        // Here I was too lazy to *also* remove that instruction so I just replace the ClearEquipped call with a Pop instruction to remove the second reference
                        yield return new(OpCodes.Pop);
                        continue;
                    }
                    // Target the second false value from the Equip call
                    if (instruction.opcode == OpCodes.Ldc_I4_0 && list[i + 2].Calls(Equip))
                    {   // Enable forceDestroyOld on the Equip call by changing from a false to a true
                        yield return new(OpCodes.Ldc_I4_1);
                        continue;
                    }

                    // Return whatever instruction is at the current index
                    yield return instruction;
                }
            }
        }
    }
}