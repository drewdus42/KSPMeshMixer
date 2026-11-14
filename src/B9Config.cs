using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSPMeshMixer
{
    /// <summary>One <c>TEXTURE</c> block of a subtype: a texture swap plus the transforms it affects.</summary>
    internal sealed class B9TextureSwitch
    {
        public List<string> Transforms = new List<string>();
        public string Texture = "";
        public string CurrentTexture = "";
        public string ShaderProperty = "_MainTex";
        public bool IsNormalMap;
    }

    /// <summary>An attach node a subtype moves, in KSP 1 part space.</summary>
    internal sealed class B9NodePosition
    {
        public string Name = "";
        public double X;
        public double Y;
        public double Z;
    }

    /// <summary>A <c>RESOURCE</c> block of either a tank type or a subtype.</summary>
    internal sealed class B9ResourceSpec
    {
        public string Name = "";
        public double? Amount;
        public double? MaxAmount;
        public double? UnitsPerVolume;
        public double? PercentFilled;
    }

    /// <summary>A resource block resolved to absolute unit counts for a specific subtype volume.</summary>
    internal sealed class B9ResolvedResource
    {
        public string Name = "";
        public double Amount;
        public double MaxAmount;
    }

    /// <summary>A <c>B9_TANK_TYPE</c> definition (or one of the built-in fallbacks).</summary>
    internal sealed class B9TankType
    {
        public string Name = "";
        public double? PercentFilled = null;
        public readonly List<B9ResourceSpec> Resources = new List<B9ResourceSpec>();
    }

    internal interface IB9TankTypeLookup
    {
        B9TankType Find(string name);
    }

    /// <summary>One <c>SUBTYPE</c> block, read from a KSP 1 <c>ModuleB9PartSwitch</c>.</summary>
    internal sealed class B9Subtype
    {
        public string Name = "";
        public string Title = "";

        /// <summary>Transforms this subtype shows. Everything else in the same switch is hidden.</summary>
        public readonly List<string> Transforms = new List<string>();

        public readonly List<B9NodePosition> Nodes = new List<B9NodePosition>();
        public readonly List<B9ResourceSpec> Resources = new List<B9ResourceSpec>();
        public readonly List<B9TextureSwitch> Textures = new List<B9TextureSwitch>();

        /// <summary>Behaviour types named by a <c>MODULE</c> block; reported, not applied yet.</summary>
        public readonly List<string> Modules = new List<string>();

        public string TankType = "";
        public double? Mass;
        public double? AddedMass;
        public double? CrashTolerance;
        public double? MaxTemp;
        public double? BaseVolume;
        public double? PercentFilled;
        public float DefaultSubtypePriority;

        /// <summary>B9 recolours the part with these; named colours are common ("Auburn", "Grey").</summary>
        public string PrimaryColor = "";

        public string SecondaryColor = "";

        public bool HasVisualData
        {
            get { return Transforms.Count > 0 || Textures.Count > 0 || Nodes.Count > 0; }
        }
    }

    /// <summary>One <c>ModuleB9PartSwitch</c>, which becomes one KSP 2 variant set.</summary>
    internal sealed class B9Switch
    {
        public string ModuleId = "";
        public string Description = "";
        public bool SwitchInFlight;
        public double? BaseVolume;

        public readonly List<B9Subtype> Subtypes = new List<B9Subtype>();

        /// <summary>Index of the subtype that is active on a freshly imported part.</summary>
        public int DefaultIndex;

        public bool HasAnythingToSwitch
        {
            get
            {
                if (Subtypes.Count < 2)
                {
                    return false;
                }

                for (int i = 0; i < Subtypes.Count; i++)
                {
                    B9Subtype subtype = Subtypes[i];
                    if (subtype.HasVisualData || subtype.Resources.Count > 0 ||
                        !string.IsNullOrEmpty(subtype.TankType) || subtype.Mass.HasValue)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Every transform this switch can show; all of them start hidden except the default's.</summary>
        public List<string> AllTransforms()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Subtypes.Count; i++)
            {
                B9Subtype subtype = Subtypes[i];
                for (int j = 0; j < subtype.Transforms.Count; j++)
                {
                    if (seen.Add(subtype.Transforms[j]))
                    {
                        result.Add(subtype.Transforms[j]);
                    }
                }

                for (int j = 0; j < subtype.Textures.Count; j++)
                {
                    for (int k = 0; k < subtype.Textures[j].Transforms.Count; k++)
                    {
                        string name = subtype.Textures[j].Transforms[k];
                        if (seen.Add(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Reads B9PartSwitch config the way B9PartSwitch does. The whole point of the mod is that
    /// existing part configs work untouched, so this accepts the KSP 1 keys as-is, including the
    /// older spellings (a subtype's mesh can be a <c>transform</c>, the profile can be
    /// <c>tankType</c> or a bare <c>RESOURCE</c> list) and the KSP 1 shader-property shortcuts.
    /// </summary>
    internal static class B9Config
    {
        public const string ModuleName = "ModuleB9PartSwitch";
        public const string DisableTransformModuleName = "ModuleB9DisableTransform";

        public static bool IsSwitch(IB9Node module)
        {
            if (module == null)
            {
                return false;
            }

            if (string.Equals(module.GetValue("name"), ModuleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A ModuleManager patch targets the module by name instead of setting it
            // ("@MODULE[ModuleB9PartSwitch]:HAS[#moduleID[x]]"); the importer resolves those before it
            // hands us a node, but reading them costs nothing and keeps raw configs readable.
            string nodeName = module.Name ?? "";
            return NameMatches(nodeName, "MODULE") &&
                   nodeName.IndexOf("[" + ModuleName + "]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Node names in the wild carry ModuleManager operators and conditions
        /// (<c>@PART[foo]</c>, <c>%SUBTYPE</c>, <c>B9_TANK_TYPE:NEEDS[X]</c>), so compare the core name.
        /// </summary>
        public static bool NameMatches(string nodeName, string expected)
        {
            if (string.IsNullOrEmpty(nodeName) || string.IsNullOrEmpty(expected))
            {
                return false;
            }

            int cut = nodeName.Length;
            int colon = nodeName.IndexOf(':');
            if (colon >= 0 && colon < cut)
            {
                cut = colon;
            }

            int bracket = nodeName.IndexOf('[');
            if (bracket >= 0 && bracket < cut)
            {
                cut = bracket;
            }

            string core = nodeName.Substring(0, cut).Trim();
            int start = 0;
            while (start < core.Length && "@+%!-".IndexOf(core[start]) >= 0)
            {
                start++;
            }

            return string.Compare(core, start, expected, 0, expected.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                   core.Length - start == expected.Length;
        }

        /// <summary>Child nodes called <paramref name="name"/>, tolerating ModuleManager decorations.</summary>
        public static IEnumerable<IB9Node> Children(IB9Node node, string name)
        {
            if (node == null)
            {
                return new IB9Node[0];
            }

            var result = new List<IB9Node>();
            foreach (IB9Node child in node.Nodes)
            {
                if (child != null && NameMatches(child.Name, name))
                {
                    result.Add(child);
                }
            }

            return result;
        }

        /// <summary>
        /// The <c>ModuleB9DisableTransform</c> transforms of a part: meshes B9 always hides, whatever
        /// subtype is active.
        /// </summary>
        public static List<string> ReadDisableTransforms(IB9Node partNode)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (partNode == null)
            {
                return result;
            }

            foreach (IB9Node module in Children(partNode, "MODULE"))
            {
                if (module == null ||
                    !string.Equals(module.GetValue("name"), DisableTransformModuleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string raw in module.GetValues("transform"))
                {
                    string name = CleanTransformName(raw);
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    {
                        result.Add(name);
                    }
                }
            }

            return result;
        }

        /// <summary>Reads one <c>ModuleB9PartSwitch</c>. Returns null when it has no subtypes.</summary>
        public static B9Switch ReadSwitch(IB9Node module)
        {
            if (module == null)
            {
                return null;
            }

            var result = new B9Switch
            {
                // The explicit id only: a fallback has to come from the caller, which knows which
                // switches it actually builds (the importer numbers them by its own count).
                ModuleId = FirstNonEmpty(module.GetValue("moduleID")),
                Description = FirstNonEmpty(module.GetValue("switcherDescription"), module.GetValue("moduleID")),
                SwitchInFlight = IsTruthy(module.GetValue("switchInFlight")),
                BaseVolume = ReadDouble(module.GetValue("baseVolume"))
            };

            foreach (IB9Node subtypeNode in Children(module, "SUBTYPE"))
            {
                B9Subtype subtype = ReadSubtype(subtypeNode, result.Subtypes.Count);
                if (subtype != null)
                {
                    result.Subtypes.Add(subtype);
                }
            }

            if (result.Subtypes.Count == 0)
            {
                return null;
            }

            result.DefaultIndex = SelectDefaultIndex(module, result.Subtypes);
            return result;
        }

        /// <summary>Convenience overload that fills in a positional id, used by the tests.</summary>
        public static B9Switch ReadSwitch(IB9Node module, int index)
        {
            B9Switch result = ReadSwitch(module);
            if (result != null && string.IsNullOrEmpty(result.ModuleId) && string.IsNullOrEmpty(result.Description))
            {
                result.ModuleId = "B9Switch" + index.ToString(CultureInfo.InvariantCulture);
                result.Description = result.ModuleId;
            }

            return result;
        }

        private static B9Subtype ReadSubtype(IB9Node node, int index)
        {
            if (node == null)
            {
                return null;
            }

            var subtype = new B9Subtype
            {
                Name = FirstNonEmpty(node.GetValue("name"), node.GetValue("title"),
                    "Subtype" + index.ToString(CultureInfo.InvariantCulture)),
                Title = node.GetValue("title", ""),
                TankType = node.GetValue("tankType", "").Trim(),
                Mass = ReadDouble(node.GetValue("mass")),
                AddedMass = ReadDouble(node.GetValue("addedMass")),
                CrashTolerance = ReadDouble(node.GetValue("crashTolerance")),
                MaxTemp = ReadDouble(node.GetValue("maxTemp")),
                BaseVolume = ReadDouble(node.GetValue("baseVolume")),
                PercentFilled = ReadDouble(node.GetValue("percentFilled")),
                PrimaryColor = (node.GetValue("primaryColor", "") ?? "").Trim(),
                SecondaryColor = (node.GetValue("secondaryColor", "") ?? "").Trim(),
                DefaultSubtypePriority = (float)(ReadDouble(node.GetValue("defaultSubtypePriority")) ?? 0.0)
            };

            foreach (string raw in node.GetValues("transform"))
            {
                string name = CleanTransformName(raw);
                if (!string.IsNullOrEmpty(name) && !subtype.Transforms.Contains(name))
                {
                    subtype.Transforms.Add(name);
                }
            }

            foreach (IB9Node nodeBlock in Children(node, "NODE"))
            {
                B9NodePosition position = ReadNode(nodeBlock);
                if (position != null)
                {
                    subtype.Nodes.Add(position);
                }
            }

            foreach (IB9Node resource in Children(node, "RESOURCE"))
            {
                B9ResourceSpec spec = ReadResource(resource);
                if (spec != null)
                {
                    subtype.Resources.Add(spec);
                }
            }

            foreach (IB9Node texture in node.Nodes)
            {
                if (texture == null ||
                    !(texture.Name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) ||
                      texture.Name.StartsWith("TEXTURE:", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                subtype.Textures.AddRange(ReadTextures(texture));
            }

            foreach (IB9Node module in Children(node, "MODULE"))
            {
                string name = module.GetValue("name");
                if (!string.IsNullOrEmpty(name))
                {
                    subtype.Modules.Add(name);
                }
            }

            return subtype;
        }

        private static B9NodePosition ReadNode(IB9Node node)
        {
            string name = node.GetValue("name");
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            double x;
            double y;
            double z;
            if (!TryParseVector3(node.GetValue("position"), out x, out y, out z))
            {
                return null;
            }

            return new B9NodePosition { Name = name.Trim(), X = x, Y = y, Z = z };
        }

        private static B9ResourceSpec ReadResource(IB9Node resource)
        {
            if (resource == null)
            {
                return null;
            }

            string name = resource.GetValue("name");
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            return new B9ResourceSpec
            {
                Name = name.Trim(),
                Amount = ReadDouble(resource.GetValue("amount")),
                MaxAmount = ReadDouble(resource.GetValue("maxAmount")),
                UnitsPerVolume = ReadDouble(resource.GetValue("unitsPerVolume")),
                PercentFilled = ReadDouble(resource.GetValue("percentFilled"))
            };
        }

        /// <summary>
        /// One <c>TEXTURE</c> block can name a texture the B9 way (<c>texture = path</c>, an optional
        /// <c>shaderProperty</c>) or with raw shader keys (<c>_MainTex = path</c>, <c>_BumpMap = path</c>).
        /// </summary>
        private static List<B9TextureSwitch> ReadTextures(IB9Node textureNode)
        {
            var result = new List<B9TextureSwitch>();
            var transforms = new List<string>();
            foreach (string raw in textureNode.GetValues("transform"))
            {
                string name = CleanTransformName(raw);
                if (!string.IsNullOrEmpty(name) && !transforms.Contains(name))
                {
                    transforms.Add(name);
                }
            }

            string currentTexture = textureNode.GetValue("currentTexture", "");
            bool isNormalMap = IsTruthy(textureNode.GetValue("isNormalMap"));

            string texture = FirstNonEmpty(textureNode.GetValue("texture"), textureNode.GetValue("newTexture"));
            if (!string.IsNullOrEmpty(texture))
            {
                string property = FirstNonEmpty(textureNode.GetValue("shaderProperty"),
                    textureNode.GetValue("textureSlotName"), isNormalMap ? "_BumpMap" : "_MainTex");
                result.Add(new B9TextureSwitch
                {
                    Transforms = new List<string>(transforms),
                    Texture = texture.Trim(),
                    CurrentTexture = currentTexture,
                    ShaderProperty = property,
                    IsNormalMap = isNormalMap || property.Equals("_BumpMap", StringComparison.OrdinalIgnoreCase)
                });
            }

            foreach (KeyValuePair<string, string> value in textureNode.Values)
            {
                if (!IsShaderTextureKey(value.Key))
                {
                    continue;
                }

                result.Add(new B9TextureSwitch
                {
                    Transforms = new List<string>(transforms),
                    Texture = (value.Value ?? "").Trim(),
                    CurrentTexture = currentTexture,
                    ShaderProperty = value.Key,
                    IsNormalMap = isNormalMap || value.Key.Equals("_BumpMap", StringComparison.OrdinalIgnoreCase)
                });
            }

            return result;
        }

        /// <summary>B9's shader-property shorthand: keys starting with <c>_</c> that name a texture slot.</summary>
        public static bool IsShaderTextureKey(string key)
        {
            if (string.IsNullOrEmpty(key) ||
                key.Equals("texture", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("newTexture", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!key.StartsWith("_", StringComparison.Ordinal))
            {
                return false;
            }

            return key.EndsWith("Tex", StringComparison.OrdinalIgnoreCase) ||
                   key.EndsWith("Map", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// B9 lets a couple of values carry a comment-ish suffix and pads names with spaces; the
        /// transform lookup itself is exact, so the name has to arrive clean.
        /// </summary>
        public static string CleanTransformName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }

            string name = raw.Trim();
            if (name.StartsWith("#", StringComparison.Ordinal))
            {
                name = name.Substring(1).Trim();
            }

            return name;
        }

        /// <summary><c>currentSubtype</c>, then the highest <c>defaultSubtypePriority</c>, then the first.</summary>
        public static int SelectDefaultIndex(IB9Node module, List<B9Subtype> subtypes)
        {
            string explicitName = FirstNonEmpty(module.GetValue("currentSubtype"), module.GetValue("defaultSubtype"));
            if (!string.IsNullOrEmpty(explicitName))
            {
                for (int i = 0; i < subtypes.Count; i++)
                {
                    if (string.Equals(subtypes[i].Name, explicitName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            int best = 0;
            for (int i = 1; i < subtypes.Count; i++)
            {
                if (subtypes[i].DefaultSubtypePriority > subtypes[best].DefaultSubtypePriority)
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Resolves the fuel of a subtype: the tank type's resources (or the subtype's own), scaled by
        /// <c>unitsPerVolume * baseVolume * percentFilled</c> exactly like B9 does.
        /// </summary>
        public static void ResolveResources(B9Switch b9Switch, B9Subtype subtype, IB9TankTypeLookup tanks,
            List<B9ResolvedResource> into)
        {
            if (b9Switch == null || subtype == null || into == null)
            {
                return;
            }

            double baseVolume = subtype.BaseVolume ?? b9Switch.BaseVolume ?? 0.0;
            if (baseVolume <= 0.0)
            {
                return;
            }

            B9TankType tankType = null;
            if (!string.IsNullOrEmpty(subtype.TankType) && tanks != null)
            {
                tankType = tanks.Find(subtype.TankType);
            }

            if (tankType != null)
            {
                double percent = subtype.PercentFilled ?? tankType.PercentFilled ?? 1.0;
                AddResources(tankType.Resources, baseVolume, percent, into);
            }

            double ownPercent = subtype.PercentFilled ?? 1.0;
            AddResources(subtype.Resources, baseVolume, ownPercent, into);
        }

        private static void AddResources(List<B9ResourceSpec> specs, double baseVolume, double percentFilled,
            List<B9ResolvedResource> into)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                B9ResourceSpec spec = specs[i];
                double maxAmount;
                if (spec.UnitsPerVolume.HasValue)
                {
                    maxAmount = spec.UnitsPerVolume.Value * baseVolume;
                }
                else if (spec.MaxAmount.HasValue)
                {
                    maxAmount = spec.MaxAmount.Value;
                }
                else
                {
                    continue;
                }

                double amount = spec.Amount ?? maxAmount * ClampPercent(spec.PercentFilled ?? percentFilled);
                into.Add(new B9ResolvedResource
                {
                    Name = spec.Name,
                    Amount = Math.Max(0.0, amount),
                    MaxAmount = Math.Max(0.0, maxAmount)
                });
            }
        }

        private static double ClampPercent(double value)
        {
            if (double.IsNaN(value) || value < 0.0)
            {
                return 0.0;
            }

            // B9 lets the value be 0..1 or 0..100.
            return value > 1.0 ? Math.Min(value / 100.0, 1.0) : Math.Min(value, 1.0);
        }

        public static bool TryParseVector3(string raw, out double x, out double y, out double z)
        {
            x = 0.0;
            y = 0.0;
            z = 0.0;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            string[] parts = raw.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            return TryParseDouble(parts[0], out x) && TryParseDouble(parts[1], out y) && TryParseDouble(parts[2], out z);
        }

        public static bool TryParseDouble(string raw, out double value)
        {
            value = 0.0;
            return !string.IsNullOrEmpty(raw) &&
                   double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static double? ReadDouble(string raw)
        {
            double value;
            return TryParseDouble(raw, out value) ? value : (double?)null;
        }

        public static bool IsTruthy(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            string text = raw.Trim();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        public static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]) && values[i].Trim().Length > 0)
                {
                    return values[i].Trim();
                }
            }

            return "";
        }
    }
}
