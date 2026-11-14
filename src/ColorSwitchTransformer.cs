using System;
using System.Collections.Generic;
using KSP.Modules;
using UnityEngine;
using VSwift.Modules.Behaviours;
using VSwift.Modules.Reverters;
using VSwift.Modules.Transformers;

namespace KSPMeshMixer
{
    /// <summary>
    /// B9 subtype colours for KSP 2. Many B9 switches ("Hull Style", OPT's heatshield noses) change
    /// nothing but <c>primaryColor</c>/<c>secondaryColor</c> — in KSP 1 that recolours the part's
    /// materials. KSP 2 has the same idea built in (<c>Module_Color</c> with a base and an accent
    /// colour), so the variant writes those two values.
    ///
    /// V-SwiFT finds transformers by scanning every loaded assembly for <see cref="Transformer"/>, which
    /// is why a mod-provided transformer survives the part JSON round-trip like the built-in ones.
    /// </summary>
    [Transformer("MeshMixerColor")]
    public sealed class ColorSwitchTransformer : ITransformer
    {
        public string Primary = "";
        public string Secondary = "";

        private IReverter _reverter;

        public IReverter Reverter
        {
            get { return _reverter ?? (_reverter = new ColorSwitchReverter()); }
        }

        public bool SavesInformation
        {
            get { return false; }
        }

        public bool VisualizesInformation
        {
            get { return false; }
        }

        public void ApplyInFlight(Module_PartSwitch partSwitch)
        {
            Apply(partSwitch);
        }

        public void ApplyInOab(Module_PartSwitch partSwitch)
        {
            Apply(partSwitch);
        }

        public void ApplyCommon(Module_PartSwitch partSwitch)
        {
            Apply(partSwitch);
        }

        private void Apply(Module_PartSwitch partSwitch)
        {
            Module_Color module = FindColorModule(partSwitch);
            if (module != null)
            {
                Color currentBase;
                Color currentAccent;
                Read(module, out currentBase, out currentAccent);

                Color baseColor;
                Color accentColor;
                if (!ColorNames.TryParse(Primary, out baseColor))
                {
                    baseColor = currentBase;
                }

                if (!ColorNames.TryParse(Secondary, out accentColor))
                {
                    accentColor = currentAccent;
                }

                if (!ColorsEqual(baseColor, currentBase) || !ColorsEqual(accentColor, currentAccent))
                {
                    // KSP 2's own paint slots. Stock-style shaders honour these; imported KSP 1
                    // materials do not, which is what Tint() below is for.
                    module.SetColors(baseColor, accentColor);
                }
            }

            if (MeshMixerSettings.Current.TintMaterials)
            {
                Tint(partSwitch);
            }
        }

        /// <summary>
        /// KSP 1 applies variant colours by tinting the part's materials (<c>_Color</c> and friends).
        /// Imported parts keep that shader, so the same tint is what makes "Hull Style" visible at all.
        /// </summary>
        private void Tint(Module_PartSwitch partSwitch)
        {
            Color primary;
            if (!ColorNames.TryParse(Primary, out primary))
            {
                return;
            }

            Color secondary;
            bool hasSecondary = ColorNames.TryParse(Secondary, out secondary);

            foreach (Renderer renderer in partSwitch.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (Material material in renderer.materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    TintMaterial(material, ColorProperty, primary);
                    if (hasSecondary)
                    {
                        TintMaterial(material, AccentProperty, secondary);
                        TintMaterial(material, AccentPropertyAlt, secondary);
                    }
                }
            }
        }

        private static void TintMaterial(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        /// <summary>Captures the colours the tint is about to overwrite, so a switch can undo it.</summary>
        internal static List<object[]> CaptureMaterialColors(Module_PartSwitch partSwitch)
        {
            var captured = new List<object[]>();
            if (partSwitch == null || !MeshMixerSettings.Current.TintMaterials)
            {
                return captured;
            }

            foreach (Renderer renderer in partSwitch.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (Material material in renderer.materials)
                {
                    if (material != null)
                    {
                        Capture(material, ColorProperty, captured);
                        Capture(material, AccentProperty, captured);
                        Capture(material, AccentPropertyAlt, captured);
                    }
                }
            }

            return captured;
        }

        private static void Capture(Material material, string property, List<object[]> into)
        {
            if (material.HasProperty(property))
            {
                into.Add(new object[] { material, property, material.GetColor(property) });
            }
        }

        internal static void RestoreMaterialColors(List<object[]> captured)
        {
            if (captured == null)
            {
                return;
            }

            for (int i = 0; i < captured.Count; i++)
            {
                var material = captured[i][0] as Material;
                string property = captured[i][1] as string;
                if (material != null && !string.IsNullOrEmpty(property) && material.HasProperty(property))
                {
                    material.SetColor(property, (Color)captured[i][2]);
                }
            }
        }

        internal const string ColorProperty = "_Color";
        internal const string AccentProperty = "_Color2";
        internal const string AccentPropertyAlt = "_SecondaryColor";

        internal static Module_Color FindColorModule(Module_PartSwitch partSwitch)
        {
            if (partSwitch == null)
            {
                return null;
            }

            Module_Color module = partSwitch.GetComponentInParent<Module_Color>(true);
            if (module == null)
            {
                module = partSwitch.GetComponentInChildren<Module_Color>(true);
            }

            return module;
        }

        internal static void Read(Module_Color module, out Color baseColor, out Color accentColor)
        {
            baseColor = Color.white;
            accentColor = Color.white;
            if (module == null)
            {
                return;
            }

            try
            {
                var colors = module.GetColors();
                baseColor = colors.Item1;
                accentColor = colors.Item2;
            }
            catch (Exception)
            {
                // A module that is still initializing: leave the defaults alone.
            }
        }

        internal static bool ColorsEqual(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.004f && Mathf.Abs(left.g - right.g) < 0.004f &&
                   Mathf.Abs(left.b - right.b) < 0.004f;
        }
    }

    /// <summary>Puts the colours back when a different variant is selected.</summary>
    public sealed class ColorSwitchReverter : IReverter
    {
        public bool RequiresInVariantSet
        {
            get { return false; }
        }

        public bool AppliesInFlight
        {
            get { return true; }
        }

        public object Store(Module_PartSwitch partSwitch)
        {
            Module_Color module = ColorSwitchTransformer.FindColorModule(partSwitch);
            Color baseColor = Color.white;
            Color accentColor = Color.white;
            if (module != null)
            {
                ColorSwitchTransformer.Read(module, out baseColor, out accentColor);
            }

            return new StoredColors
            {
                ModuleBase = ColorNames.ToHex(baseColor),
                ModuleAccent = ColorNames.ToHex(accentColor),
                Materials = ColorSwitchTransformer.CaptureMaterialColors(partSwitch)
            };
        }

        public void Revert(Module_PartSwitch partSwitch, object data, bool isStartingReset)
        {
            var stored = data as StoredColors;
            if (stored == null)
            {
                return;
            }

            Module_Color module = ColorSwitchTransformer.FindColorModule(partSwitch);
            Color baseColor;
            Color accentColor;
            if (module != null &&
                ColorNames.TryParse(stored.ModuleBase, out baseColor) &&
                ColorNames.TryParse(stored.ModuleAccent, out accentColor))
            {
                module.SetColors(baseColor, accentColor);
            }

            ColorSwitchTransformer.RestoreMaterialColors(stored.Materials);
        }

        private sealed class StoredColors
        {
            public string ModuleBase = "";
            public string ModuleAccent = "";
            public List<object[]> Materials;
        }
    }
}
