using System;
using System.Collections.Generic;
using HarmonyLib;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using VSwift.Modules.Data;

namespace KSPMeshMixer
{
    /// <summary>
    /// Takes over the importer's B9 conversion. The importer only builds a switch when a subtype names
    /// transforms, textures or nodes; fuel-only subtypes therefore vanish, and resources/mass are never
    /// switched. Returning false with our own data replaces that conversion for the part.
    /// </summary>
    [HarmonyPatch(typeof(Ksp1B9PartSwitchVariantBuilder), nameof(Ksp1B9PartSwitchVariantBuilder.TryBuild))]
    internal static class B9TryBuildPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Ksp1ConfigFile file, Ksp1ConfigNode partNode, float rescaleFactor,
            Ksp1PartImportContext context, ref Data_PartSwitch data, ref List<string> baselineInactiveTransforms,
            ref bool __result)
        {
            if (!MeshMixerSettings.Current.Enabled || partNode == null)
            {
                return true;
            }

            try
            {
                Data_PartSwitch built;
                List<string> inactive;
                if (!B9VariantBuilder.TryBuild(file, partNode, rescaleFactor, context, out built, out inactive))
                {
                    // Nothing this mod handles better: let the importer do its own conversion.
                    return true;
                }

                data = built;
                baselineInactiveTransforms = inactive;
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                MeshMixerLog.Error("Could not convert the B9 switches of '" +
                                   (context == null ? "?" : context.PartName) +
                                   "'; falling back to the importer.", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Adds the labels for the switches only this mod builds (the importer's own pass skips them), using
    /// its key scheme so nothing else has to change.
    /// </summary>
    [HarmonyPatch(typeof(Ksp1B9PartSwitchVariantBuilder), nameof(Ksp1B9PartSwitchVariantBuilder.AddLocalizationTerms))]
    internal static class B9LocalizationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Ksp1ConfigNode partNode, string partName, Func<string, string> resolve,
            Action<string, string> add)
        {
            if (!MeshMixerSettings.Current.Enabled || partNode == null || string.IsNullOrEmpty(partName) ||
                add == null)
            {
                return;
            }

            try
            {
                List<BuiltVariantSet> sets = B9VariantBuilder.GetBuiltSets(partName);
                if (sets == null)
                {
                    return;
                }

                for (int i = 0; i < sets.Count; i++)
                {
                    BuiltVariantSet set = sets[i];
                    if (set.HadVisualData && !set.NeedsLabels)
                    {
                        // The importer registers these itself; adding them twice would be noise.
                        continue;
                    }

                    add(B9VariantBuilder.VariantSetKey(partName, set.VariantSetId),
                        Localize(resolve, set.VariantSetLabel));
                    for (int j = 0; j < set.VariantIds.Count; j++)
                    {
                        add(B9VariantBuilder.VariantKey(partName, set.VariantSetId, set.VariantIds[j]),
                            Localize(resolve, set.VariantLabels[j]));
                    }
                }
            }
            catch (Exception ex)
            {
                MeshMixerLog.Warn("Could not register the variant labels of '" + partName + "': " + ex.Message);
            }
        }

        private static string Localize(Func<string, string> resolve, string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return "";
            }

            try
            {
                string resolved = resolve == null ? null : resolve(label);
                return string.IsNullOrEmpty(resolved) ? label : resolved;
            }
            catch (Exception)
            {
                return label;
            }
        }
    }
}
