using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using SpaceWarp2.API.Mods;

namespace KSPMeshMixer
{
    /// <summary>
    /// SpaceWarp entry point. Hooks the KSP1 importer's B9PartSwitch conversion so existing KSP 1 part
    /// configs — <c>ModuleB9PartSwitch</c> with <c>SUBTYPE</c> blocks — keep working in KSP 2 without
    /// touching the parts themselves.
    /// </summary>
    public sealed class MeshMixerMod : MonoBehaviourMod
    {
        public const string Version = "0.1.0";

        public static MeshMixerMod Instance { get; private set; }

        public override void OnPreInitialized()
        {
            Instance = this;

            string folder = null;
            try
            {
                folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception ex)
            {
                MeshMixerLog.Warn("Could not resolve the mod folder: " + ex.Message);
            }

            MeshMixerSettings.Load(folder);
        }

        public override void OnInitialized()
        {
            try
            {
                ApplyPatches();
                MeshMixerLog.Report("KSP MeshMixer " + Version + " ready (B9PartSwitch conversion " +
                                    (MeshMixerSettings.Current.Enabled ? "active" : "disabled") + ").");
            }
            catch (Exception ex)
            {
                MeshMixerLog.Error("Failed to start KSP MeshMixer.", ex);
            }
        }

        /// <summary>
        /// Each patch is applied on its own: Harmony's PatchAll drops the whole batch when one target
        /// cannot be prepared, which would silently leave every part unswitched.
        /// </summary>
        private void ApplyPatches()
        {
            Harmony harmony = new Harmony(SWMetadata == null ? "KSPMeshMixer" : SWMetadata.Guid);
            Type[] patches =
            {
                typeof(B9TryBuildPatch),
                typeof(B9LocalizationPatch)
            };

            int applied = 0;
            for (int i = 0; i < patches.Length; i++)
            {
                try
                {
                    harmony.CreateClassProcessor(patches[i]).Patch();
                    applied++;
                }
                catch (Exception ex)
                {
                    MeshMixerLog.Error("Could not apply the " + patches[i].Name + " hook.", ex);
                }
            }

            MeshMixerLog.Report("Applied " + applied + " of " + patches.Length + " Harmony hook(s).");
        }
    }
}
