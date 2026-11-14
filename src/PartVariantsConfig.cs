using System;
using System.Collections.Generic;

namespace KSPMeshMixer
{
    /// <summary>One KSP 1 <c>VARIANT</c> block of <c>ModulePartVariants</c>.</summary>
    internal sealed class PartVariant
    {
        public string Name = "";
        public string Title = "";
        public string ThemeName = "";
        public string PrimaryColor = "";
        public string SecondaryColor = "";
        public readonly List<B9TextureSwitch> Textures = new List<B9TextureSwitch>();

        /// <summary>Behaviour types named by a <c>MODULE</c> override block; reported, not applied yet.</summary>
        public readonly List<string> Modules = new List<string>();

        /// <summary>
        /// The <c>GAMEOBJECTS</c> block: which named objects this variant shows. This is how stock
        /// parts get alternate *meshes* (one hull per style), so it is the mesh switch.
        /// </summary>
        public readonly List<KeyValuePair<string, bool>> GameObjects = new List<KeyValuePair<string, bool>>();
    }

    /// <summary>
    /// KSP 1's own variant module (<c>ModulePartVariants</c>), which is what stock, DLC and ReStock
    /// parts use for their alternate stylings: the part keeps one model and each variant swaps textures
    /// and the two colour slots.
    /// </summary>
    internal sealed class PartVariantSwitch
    {
        public string ModuleId = "";
        public string BaseDisplayName = "";
        public string BaseThemeName = "";
        public string PrimaryColor = "";
        public string SecondaryColor = "";

        /// <summary><c>baseVariant</c>: the variant a freshly built part starts in.</summary>
        public string BaseVariant = "";

        public readonly List<PartVariant> Variants = new List<PartVariant>();

        public bool HasAnythingToSwitch
        {
            get { return Variants.Count > 0; }
        }
    }

    internal static class PartVariantsConfig
    {
        public const string ModuleName = "ModulePartVariants";

        public static bool IsVariants(IB9Node module)
        {
            if (module == null)
            {
                return false;
            }

            if (string.Equals(module.GetValue("name"), ModuleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // ModuleManager patches target the module by name instead of setting it.
            string nodeName = module.Name ?? "";
            return B9Config.NameMatches(nodeName, "MODULE") &&
                   nodeName.IndexOf("[" + ModuleName + "]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static PartVariantSwitch Read(IB9Node module)
        {
            if (module == null)
            {
                return null;
            }

            var result = new PartVariantSwitch
            {
                ModuleId = B9Config.FirstNonEmpty(module.GetValue("moduleID")),
                BaseDisplayName = (module.GetValue("baseDisplayName", "") ?? "").Trim(),
                BaseThemeName = (module.GetValue("baseThemeName", "") ?? "").Trim(),
                PrimaryColor = (module.GetValue("primaryColor", "") ?? "").Trim(),
                SecondaryColor = (module.GetValue("secondaryColor", "") ?? "").Trim(),
                BaseVariant = (module.GetValue("baseVariant", "") ?? "").Trim()
            };

            foreach (IB9Node variantNode in B9Config.Children(module, "VARIANT"))
            {
                PartVariant variant = ReadVariant(variantNode, result.Variants.Count);
                if (variant != null)
                {
                    result.Variants.Add(variant);
                }
            }

            return result.Variants.Count == 0 ? null : result;
        }

        private static PartVariant ReadVariant(IB9Node node, int index)
        {
            if (node == null)
            {
                return null;
            }

            var variant = new PartVariant
            {
                Name = B9Config.FirstNonEmpty(node.GetValue("name"), node.GetValue("displayName"),
                    "Variant" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Title = (node.GetValue("displayName", "") ?? "").Trim(),
                ThemeName = (node.GetValue("themeName", "") ?? "").Trim(),
                PrimaryColor = (node.GetValue("primaryColor", "") ?? "").Trim(),
                SecondaryColor = (node.GetValue("secondaryColor", "") ?? "").Trim()
            };

            foreach (IB9Node texture in node.Nodes)
            {
                if (texture == null ||
                    !(texture.Name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) ||
                      texture.Name.StartsWith("TEXTURE:", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                variant.Textures.AddRange(ReadTexture(texture));
            }

            foreach (IB9Node module in B9Config.Children(node, "MODULE"))
            {
                string name = module.GetValue("name");
                if (!string.IsNullOrEmpty(name))
                {
                    variant.Modules.Add(name);
                }
            }

            foreach (IB9Node gameObjects in B9Config.Children(node, "GAMEOBJECTS"))
            {
                foreach (KeyValuePair<string, string> entry in gameObjects.Values)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                    {
                        continue;
                    }

                    variant.GameObjects.Add(new KeyValuePair<string, bool>(
                        entry.Key.Trim(), B9Config.IsTruthy(entry.Value)));
                }
            }

            return variant;
        }

        /// <summary>
        /// A <c>MODULE</c>-style variant texture block. Unlike B9 these name the material and use
        /// <c>mainTextureURL</c>; the direct shader keys work as well.
        /// </summary>
        private static List<B9TextureSwitch> ReadTexture(IB9Node textureNode)
        {
            var result = new List<B9TextureSwitch>();
            string materialName = (textureNode.GetValue("materialName", "") ?? "").Trim();
            var transforms = new List<string>();
            if (!string.IsNullOrEmpty(materialName))
            {
                // Material names are not transforms: keep them out of the transform list and let the
                // texture switch target the whole part, which is what KSP 1 does for these variants.
            }

            string main = B9Config.FirstNonEmpty(textureNode.GetValue("mainTextureURL"),
                textureNode.GetValue("texture"), textureNode.GetValue("newTexture"));
            if (!string.IsNullOrEmpty(main))
            {
                result.Add(new B9TextureSwitch
                {
                    Transforms = new List<string>(transforms),
                    Texture = main.Trim(),
                    CurrentTexture = "",
                    ShaderProperty = "_MainTex",
                    IsNormalMap = false
                });
            }

            foreach (KeyValuePair<string, string> value in textureNode.Values)
            {
                if (!B9Config.IsShaderTextureKey(value.Key) ||
                    value.Key.Equals("mainTextureURL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(new B9TextureSwitch
                {
                    Transforms = new List<string>(transforms),
                    Texture = (value.Value ?? "").Trim(),
                    CurrentTexture = "",
                    ShaderProperty = value.Key,
                    IsNormalMap = value.Key.Equals("_BumpMap", StringComparison.OrdinalIgnoreCase)
                });
            }

            return result;
        }
    }
}
