using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KSP.Sim;
using KSP.Sim.ResourceSystem;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;
using VSwift.Modules.Data;
using VSwift.Modules.Transformers;
using VSwift.Modules.Variants;

namespace KSPMeshMixer
{
    /// <summary>What a part's variant sets ended up as, so the labels can be registered later.</summary>
    internal sealed class BuiltVariantSet
    {
        public string VariantSetId = "";
        public string VariantSetLabel = "";
        public readonly List<string> VariantIds = new List<string>();
        public readonly List<string> VariantLabels = new List<string>();
        public readonly List<string> TransformNames = new List<string>();
        public bool HadVisualData;

        /// <summary>
        /// True for sets the importer's own pass never registers labels for (stock
        /// <c>ModulePartVariants</c>), so this mod has to add them itself.
        /// </summary>
        public bool NeedsLabels;

        /// <summary>The variant a stock-style part should start in (its <c>baseVariant</c>).</summary>
        public string DefaultVariantId = "";
    }

    /// <summary>
    /// Turns KSP 1 <c>ModuleB9PartSwitch</c> blocks into the V-SwiFT variant sets KSP 2 uses. This is a
    /// superset of what the KSP1 importer does on its own: the importer only builds a switch when a
    /// subtype names transforms, textures or nodes, so fuel-only subtypes (and the tanks that rely on
    /// them) never appear, and it never switches resources or mass.
    /// </summary>
    internal static class B9VariantBuilder
    {
        /// <summary>Variant sets built this session, per part, for the localization hook.</summary>
        private static readonly Dictionary<string, List<BuiltVariantSet>> BuiltSets =
            new Dictionary<string, List<BuiltVariantSet>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryBuild(Ksp1ConfigFile file, Ksp1ConfigNode partNode, float rescaleFactor,
            Ksp1PartImportContext context, out Data_PartSwitch data, out List<string> baselineInactive)
        {
            data = new Data_PartSwitch();
            baselineInactive = B9Config.ReadDisableTransforms(Ksp1NodeAdapter.Wrap(partNode));
            if (partNode == null || context == null)
            {
                data = null;
                return false;
            }

            string partName = context.PartName ?? partNode.GetValue("name", "");
            var inactive = new HashSet<string>(baselineInactive, StringComparer.Ordinal);
            var usedSetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var built = new List<BuiltVariantSet>();
            double baseMass = B9Config.ReadDouble(partNode.GetValue("mass")) ?? 0.0;
            string gameDataRoot = file != null && file.Root != null ? file.Root.GameDataRoot : "";
            string localDirectory = file == null ? "" : Path.GetDirectoryName(file.Path);

            int moduleIndex = 0;
            int visualIndex = 0;
            int fuelIndex = 0;
            int styleIndex = 0;
            foreach (IB9Node moduleNode in B9Config.Children(Ksp1NodeAdapter.Wrap(partNode), "MODULE"))
            {
                if (PartVariantsConfig.IsVariants(moduleNode))
                {
                    VariantSet styleSet = BuildPartVariantsSet(moduleNode, partName, gameDataRoot, localDirectory,
                        styleIndex, usedSetIds, inactive, baselineInactive, out BuiltVariantSet styleInfo);
                    if (styleSet == null || styleSet.Variants.Count == 0)
                    {
                        continue;
                    }

                    styleIndex++;
                    built.Add(styleInfo);
                    data.VariantSets.Add(styleSet);
                    string styleDefault = styleInfo.DefaultVariantId;
                    if (string.IsNullOrEmpty(styleDefault))
                    {
                        styleDefault = styleSet.Variants[0].VariantId;
                    }

                    data.DefaultActiveVariants.Add(styleDefault);
                    data.ActiveVariants.Add(styleDefault);
                    continue;
                }

                if (!B9Config.IsSwitch(moduleNode))
                {
                    continue;
                }

                moduleIndex++;
                B9Switch b9Switch = B9Config.ReadSwitch(moduleNode);
                if (b9Switch == null || !b9Switch.HasAnythingToSwitch)
                {
                    continue;
                }

                // The importer names an unnamed switch "B9Switch<n>" counting only the switches it
                // builds itself (the ones with visuals). Matching that keeps its labels valid for those,
                // while fuel-only switches get a namespace of their own.
                bool hasVisuals = HasVisuals(b9Switch);
                string fallback = hasVisuals
                    ? "B9Switch" + visualIndex.ToString(CultureInfo.InvariantCulture)
                    : "B9Fuel" + fuelIndex.ToString(CultureInfo.InvariantCulture);
                if (hasVisuals)
                {
                    visualIndex++;
                }
                else
                {
                    fuelIndex++;
                }

                VariantSet variantSet = BuildVariantSet(b9Switch, fallback, partName, gameDataRoot, localDirectory,
                    rescaleFactor, baseMass, usedSetIds, out BuiltVariantSet info);
                if (variantSet == null || variantSet.Variants.Count == 0)
                {
                    continue;
                }

                foreach (string transform in b9Switch.AllTransforms())
                {
                    if (inactive.Add(transform))
                    {
                        baselineInactive.Add(transform);
                    }
                }

                info.TransformNames.AddRange(b9Switch.AllTransforms());
                built.Add(info);
                data.VariantSets.Add(variantSet);

                string defaultId = variantSet.Variants[Math.Min(b9Switch.DefaultIndex, variantSet.Variants.Count - 1)].VariantId;
                data.DefaultActiveVariants.Add(defaultId);
                data.ActiveVariants.Add(defaultId);
            }

            if (data.VariantSets.Count == 0)
            {
                data = null;
                return false;
            }

            BuiltSets[partName] = built;
            MeshMixerLog.Info("'" + partName + "': built " + data.VariantSets.Count +
                              " B9 switch(es) from " + moduleIndex + " ModuleB9PartSwitch block(s).");
            return true;
        }

        private static VariantSet BuildVariantSet(B9Switch b9Switch, string fallbackId, string partName, string gameDataRoot,
            string localDirectory, float rescaleFactor, double baseMass, HashSet<string> usedSetIds,
            out BuiltVariantSet info)
        {
            info = new BuiltVariantSet
            {
                VariantSetId = MakeUnique(
                    B9Config.FirstNonEmpty(b9Switch.ModuleId, b9Switch.Description, fallbackId), usedSetIds),
                VariantSetLabel = B9Config.FirstNonEmpty(b9Switch.Description, b9Switch.ModuleId, fallbackId)
            };

            var variantSet = new VariantSet
            {
                VariantSetId = info.VariantSetId,
                VariantSetLocalizationKey = VariantSetKey(partName, info.VariantSetId),
                IsPopout = false
            };

            // Resources any subtype of this switch can add: switching has to clear them out first.
            List<string> managedResources = ManagedResourceNames(b9Switch);

            var usedVariantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < b9Switch.Subtypes.Count; i++)
            {
                B9Subtype subtype = b9Switch.Subtypes[i];
                if (subtype.HasVisualData)
                {
                    info.HadVisualData = true;
                }

                string variantId = MakeUnique(B9Config.FirstNonEmpty(subtype.Name, subtype.Title,
                    "Subtype" + i.ToString(CultureInfo.InvariantCulture)), usedVariantIds);
                var variant = new Variant
                {
                    VariantId = variantId,
                    VariantLocalizationKey = VariantKey(partName, info.VariantSetId, variantId)
                };

                info.VariantIds.Add(variantId);
                info.VariantLabels.Add(B9Config.FirstNonEmpty(subtype.Title, subtype.Name, variantId));

                AddTransformers(variant, b9Switch, subtype, gameDataRoot, localDirectory, rescaleFactor,
                    baseMass, managedResources);
                variantSet.Variants.Add(variant);
            }

            return variantSet;
        }

        private static void AddTransformers(Variant variant, B9Switch b9Switch, B9Subtype subtype,
            string gameDataRoot, string localDirectory, float rescaleFactor, double baseMass,
            List<string> managedResources)
        {
            if (subtype.Transforms.Count > 0)
            {
                variant.Transformers.Add(new TransformActivator
                {
                    Transforms = new List<string>(subtype.Transforms)
                });
            }

            if (subtype.Nodes.Count > 0)
            {
                var mover = new AttachNodeMover();
                for (int i = 0; i < subtype.Nodes.Count; i++)
                {
                    B9NodePosition node = subtype.Nodes[i];
                    mover.MovedNodes[node.Name] = new Vector3d(
                        node.X * rescaleFactor,
                        node.Y * rescaleFactor,
                        node.Z * rescaleFactor);
                }

                variant.Transformers.Add(mover);
            }

            List<Ksp1TextureSwitch> textures = BuildTextureSwitches(subtype, gameDataRoot, localDirectory);
            if (textures.Count > 0 && MeshMixerSettings.Current.SwitchTextures)
            {
                var transformer = new Ksp1TextureSwitchTransformer();
                transformer.Switches.AddRange(textures);
                variant.Transformers.Add(transformer);
            }

            if (MeshMixerSettings.Current.SwitchResources && managedResources.Count > 0)
            {
                var resolved = new List<B9ResolvedResource>();
                B9Config.ResolveResources(b9Switch, subtype, TankCatalog.Instance, resolved);

                // Always clear the switch's resources: otherwise picking a structural (empty) subtype
                // would leave the previous subtype's fuel in the tank.
                variant.Transformers.Add(new ResourceContainerRemover
                {
                    Containers = new List<string>(managedResources)
                });

                var adder = new ResourceContainerAdder();
                for (int i = 0; i < resolved.Count; i++)
                {
                    string name = Ksp1ResourceMapper.MapResourceName(resolved[i].Name);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    adder.Containers.Add(new ContainedResourceDefinition(resolved[i].MaxAmount, name)
                    {
                        initialUnits = resolved[i].Amount
                    });
                }

                if (adder.Containers.Count > 0)
                {
                    variant.Transformers.Add(adder);
                }
            }

            if (MeshMixerSettings.Current.SwitchMass)
            {
                double? subtypeMass = subtype.Mass ?? subtype.AddedMass;
                if (subtypeMass.HasValue)
                {
                    // addedMass is already a delta; mass is absolute.
                    float modifier = subtype.Mass.HasValue
                        ? (float)(subtype.Mass.Value - baseMass)
                        : (float)subtype.AddedMass.Value;
                    if (Math.Abs(modifier) > 0.0005f)
                    {
                        variant.Transformers.Add(new MassModifier { Modifier = modifier });
                    }
                }
            }

            AddColorTransformer(variant, subtype.PrimaryColor, subtype.SecondaryColor);
        }

        /// <summary>Many B9 switches only recolour the part, so a colour transformer is the whole effect.</summary>
        private static void AddColorTransformer(Variant variant, string primary, string secondary)
        {
            if (!MeshMixerSettings.Current.SwitchColors)
            {
                return;
            }

            bool hasPrimary = HasKnownColor(primary);
            bool hasSecondary = HasKnownColor(secondary);
            if (!hasPrimary && !hasSecondary)
            {
                return;
            }

            variant.Transformers.Add(new ColorSwitchTransformer
            {
                Primary = hasPrimary ? primary.Trim() : "",
                Secondary = hasSecondary ? secondary.Trim() : ""
            });
        }

        private static bool HasKnownColor(string raw)
        {
            UnityEngine.Color color;
            return !string.IsNullOrEmpty(raw) && ColorNames.TryParse(raw, out color);
        }

        /// <summary>True when any subtype names transforms, textures or nodes.</summary>
        private static bool HasVisuals(B9Switch b9Switch)
        {
            for (int i = 0; i < b9Switch.Subtypes.Count; i++)
            {
                if (b9Switch.Subtypes[i].HasVisualData)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Every resource name this switch may add, in KSP 2 terms, so a switch can clear them.</summary>
        private static List<string> ManagedResourceNames(B9Switch b9Switch)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolved = new List<B9ResolvedResource>();
            for (int i = 0; i < b9Switch.Subtypes.Count; i++)
            {
                resolved.Clear();
                B9Config.ResolveResources(b9Switch, b9Switch.Subtypes[i], TankCatalog.Instance, resolved);
                for (int j = 0; j < resolved.Count; j++)
                {
                    string name = Ksp1ResourceMapper.MapResourceName(resolved[j].Name);
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// Stock <c>ModulePartVariants</c>: a base variant plus one entry per <c>VARIANT</c> block. The
        /// base carries the module's own colours and no texture swap, exactly like KSP 1's default.
        /// </summary>
        private static VariantSet BuildPartVariantsSet(IB9Node moduleNode, string partName, string gameDataRoot,
            string localDirectory, int index, HashSet<string> usedSetIds, HashSet<string> inactive,
            List<string> baselineInactive, out BuiltVariantSet info)
        {
            info = null;
            PartVariantSwitch variants = PartVariantsConfig.Read(moduleNode);
            if (variants == null || !variants.HasAnythingToSwitch)
            {
                return null;
            }

            string fallback = "Style" + (index == 0 ? "" : index.ToString(CultureInfo.InvariantCulture));
            info = new BuiltVariantSet
            {
                VariantSetId = MakeUnique(B9Config.FirstNonEmpty(variants.ModuleId, fallback), usedSetIds),
                VariantSetLabel = "Style",
                HadVisualData = true,
                NeedsLabels = true
            };

            var variantSet = new VariantSet
            {
                VariantSetId = info.VariantSetId,
                VariantSetLocalizationKey = VariantSetKey(partName, info.VariantSetId),
                IsPopout = false
            };

            // Every style's meshes load, so all of them have to start hidden: that is what keeps the
            // parts-grid thumbnails and the placed part clean instead of z-fighting. Each variant then
            // switches on just its own objects (ModulePartVariants' GAMEOBJECTS block).
            for (int i = 0; i < variants.Variants.Count; i++)
            {
                List<KeyValuePair<string, bool>> objects = variants.Variants[i].GameObjects;
                for (int j = 0; j < objects.Count; j++)
                {
                    string name = objects[j].Key;
                    if (!string.IsNullOrEmpty(name) && inactive.Add(name))
                    {
                        baselineInactive.Add(name);
                        info.TransformNames.Add(name);
                    }
                }
            }

            int baseIndex = FindBaseVariant(variants);
            if (baseIndex < 0)
            {
                // No baseVariant: offer the module's own look first, like KSP 1 does.
                var baseVariant = new Variant
                {
                    VariantId = "Default",
                    VariantLocalizationKey = VariantKey(partName, info.VariantSetId, "Default")
                };
                AddColorTransformer(baseVariant, variants.PrimaryColor, variants.SecondaryColor);
                variantSet.Variants.Add(baseVariant);
                info.VariantIds.Add("Default");
                info.VariantLabels.Add(B9Config.FirstNonEmpty(variants.BaseDisplayName, "Default"));
                info.DefaultVariantId = "Default";
            }

            for (int i = 0; i < variants.Variants.Count; i++)
            {
                PartVariant source = variants.Variants[i];
                string variantId = MakeUnique(source.Name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var variant = new Variant
                {
                    VariantId = variantId,
                    VariantLocalizationKey = VariantKey(partName, info.VariantSetId, source.Name)
                };

                AddColorTransformer(variant,
                    B9Config.FirstNonEmpty(source.PrimaryColor, variants.PrimaryColor),
                    B9Config.FirstNonEmpty(source.SecondaryColor, variants.SecondaryColor));

                List<string> shown = ShownObjects(source);
                if (shown.Count > 0)
                {
                    variant.Transformers.Add(new TransformActivator { Transforms = shown });
                }

                List<Ksp1TextureSwitch> textures = BuildTextureSwitches(source.Textures, gameDataRoot, localDirectory);
                if (textures.Count > 0 && MeshMixerSettings.Current.SwitchTextures)
                {
                    var transformer = new Ksp1TextureSwitchTransformer();
                    transformer.Switches.AddRange(textures);
                    variant.Transformers.Add(transformer);
                }

                variantSet.Variants.Add(variant);
                info.VariantIds.Add(variant.VariantId);
                info.VariantLabels.Add(B9Config.FirstNonEmpty(source.Title, source.Name, variant.VariantId));
                if (i == baseIndex)
                {
                    info.DefaultVariantId = variant.VariantId;
                }
            }

            MeshMixerLog.Info("'" + partName + "': " + variants.Variants.Count +
                              " KSP1 style variant(s) converted from ModulePartVariants" +
                              (info.TransformNames.Count == 0 ? "" : ", " + info.TransformNames.Count + " style mesh(es)") +
                              ".");
            return variantSet;
        }

        /// <summary>Index of the variant named by <c>baseVariant</c>, or -1.</summary>
        private static int FindBaseVariant(PartVariantSwitch variants)
        {
            if (string.IsNullOrEmpty(variants.BaseVariant))
            {
                return -1;
            }

            for (int i = 0; i < variants.Variants.Count; i++)
            {
                if (string.Equals(variants.Variants[i].Name, variants.BaseVariant, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The <c>GAMEOBJECTS</c> entries a variant switches on.</summary>
        private static List<string> ShownObjects(PartVariant source)
        {
            var shown = new List<string>();
            for (int i = 0; i < source.GameObjects.Count; i++)
            {
                if (source.GameObjects[i].Value && !string.IsNullOrEmpty(source.GameObjects[i].Key))
                {
                    shown.Add(source.GameObjects[i].Key);
                }
            }

            return shown;
        }

        private static List<Ksp1TextureSwitch> BuildTextureSwitches(B9Subtype subtype, string gameDataRoot,
            string localDirectory)
        {
            return BuildTextureSwitches(subtype.Textures, gameDataRoot, localDirectory);
        }

        private static List<Ksp1TextureSwitch> BuildTextureSwitches(List<B9TextureSwitch> source, string gameDataRoot,
            string localDirectory)
        {
            var result = new List<Ksp1TextureSwitch>();
            for (int i = 0; i < source.Count; i++)
            {
                B9TextureSwitch texture = source[i];
                if (string.IsNullOrEmpty(texture.Texture))
                {
                    continue;
                }

                var entry = new Ksp1TextureSwitch
                {
                    GameDataRoot = gameDataRoot ?? "",
                    LocalDirectory = localDirectory ?? "",
                    TexturePath = texture.Texture,
                    CurrentTexture = texture.CurrentTexture ?? "",
                    ShaderProperty = B9Config.FirstNonEmpty(texture.ShaderProperty, texture.IsNormalMap ? "_BumpMap" : "_MainTex"),
                    IsNormalMap = texture.IsNormalMap
                };
                entry.Transforms.AddRange(texture.Transforms);
                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// The importer's own naming scheme, so labels line up with the terms it registers
        /// (<c>KSP1/Parts/&lt;part&gt;/Variants/&lt;set&gt;/&lt;variant&gt;/Name</c>).
        /// </summary>
        public static string VariantSetKey(string partName, string variantSetId)
        {
            return "KSP1/Parts/" + partName + "/Variants/" + variantSetId + "/Name";
        }

        public static string VariantKey(string partName, string variantSetId, string variantId)
        {
            return "KSP1/Parts/" + partName + "/Variants/" + variantSetId + "/" + variantId + "/Name";
        }

        public static List<BuiltVariantSet> GetBuiltSets(string partName)
        {
            List<BuiltVariantSet> sets;
            return partName != null && BuiltSets.TryGetValue(partName, out sets) ? sets : null;
        }

        private static string MakeUnique(string value, HashSet<string> used)
        {
            string baseName = string.IsNullOrEmpty(value) || value.Trim().Length == 0 ? "Variant" : value.Trim();
            string candidate = baseName;
            int suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }
    }
}
