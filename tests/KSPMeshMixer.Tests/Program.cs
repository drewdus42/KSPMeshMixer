using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Redux.Ksp1Import.Config;

namespace KSPMeshMixer.Tests
{
    /// <summary>
    /// Checks the B9PartSwitch reading layer: synthetic configs for the exact semantics, then the part
    /// configs that actually ship in the user's KSP 1 folder, because "works with the old configs" is
    /// the whole point.
    /// </summary>
    internal static class Program
    {
        private const string Ksp1Root =
            @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program 2\mods\KSP1";

        private static int _failures;

        private static int Main()
        {
            try
            {
                ReadsBasicSwitch();
                ReadsTransformsAndLooksThemUp();
                PicksTheDefaultSubtype();
                ReadsTextures();
                ReadsColorsAndAddedMass();
                ReadsStockPartVariants();
                ReadsStockVariantGameObjects();
                ReadsDisableTransforms();
                KnowsWhenThereIsNothingToSwitch();
                ResolvesTankResources();
                ResolvesPercentFilled();
                HandlesBrokenInput();
                RealPartConfigs();
            }
            finally
            {
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "All tests passed." : _failures + " test(s) failed.");
            return _failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ synthetic

        private static void ReadsBasicSwitch()
        {
            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                moduleID = tankSwitch
                switcherDescription = Tank type
                baseVolume = 800
                switchInFlight = True
                SUBTYPE
                {
                    name = LFO
                    tankType = LFO
                }
                SUBTYPE
                {
                    name = LF
                    tankType = LF
                }");

            B9Switch b9 = B9Config.ReadSwitch(part.GetNode("MODULE"), 0);
            Check("switch is read", b9 != null);
            if (b9 == null)
            {
                return;
            }

            Check("module id is read", b9.ModuleId == "tankSwitch");
            Check("description is read", b9.Description == "Tank type");
            Check("base volume is read", Near(b9.BaseVolume ?? 0, 800));
            Check("switch in flight is read", b9.SwitchInFlight);
            Check("both subtypes are read", b9.Subtypes.Count == 2);
            Check("fuel-only switch still has something to switch", b9.HasAnythingToSwitch);
        }

        private static void ReadsTransformsAndLooksThemUp()
        {
            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                SUBTYPE
                {
                    name = Short
                    transform = shortTank
                    transform = shortTank2
                }
                SUBTYPE
                {
                    name = Long
                    transform = longTank
                }");

            B9Switch b9 = B9Config.ReadSwitch(part.GetNode("MODULE"), 0);
            Check("two subtypes", b9 != null && b9.Subtypes.Count == 2);
            if (b9 == null)
            {
                return;
            }

            Check("transform list is read", b9.Subtypes[0].Transforms.Count == 2 &&
                                            b9.Subtypes[0].Transforms[0] == "shortTank" &&
                                            b9.Subtypes[0].Transforms[1] == "shortTank2");
            Check("visual data is detected", b9.Subtypes[0].HasVisualData && b9.Subtypes[1].HasVisualData);

            List<string> all = b9.AllTransforms();
            Check("all transforms are collected once", all.Count == 3 &&
                                                       all.Contains("shortTank") && all.Contains("shortTank2") &&
                                                       all.Contains("longTank"));

            FakeNode padded = PartWithModule(@"
                name = ModuleB9PartSwitch
                SUBTYPE
                {
                    name = A
                    transform =   spacedName
                    transform = #hashedName
                }
                SUBTYPE
                {
                    name = B
                    transform = other
                }");
            B9Switch paddedSwitch = B9Config.ReadSwitch(padded.GetNode("MODULE"), 0);
            Check("transform names are cleaned", paddedSwitch != null &&
                                                paddedSwitch.Subtypes[0].Transforms[0] == "spacedName" &&
                                                paddedSwitch.Subtypes[0].Transforms[1] == "hashedName");
        }

        private static void PicksTheDefaultSubtype()
        {
            FakeNode byPriority = PartWithModule(@"
                name = ModuleB9PartSwitch
                SUBTYPE { name = A; defaultSubtypePriority = 1 }
                SUBTYPE { name = B; defaultSubtypePriority = 5 }
                SUBTYPE { name = C }");
            Check("highest priority wins",
                B9Config.ReadSwitch(byPriority.GetNode("MODULE"), 0).DefaultIndex == 1);

            FakeNode byCurrent = PartWithModule(@"
                name = ModuleB9PartSwitch
                currentSubtype = C
                SUBTYPE { name = A; defaultSubtypePriority = 9 }
                SUBTYPE { name = B }
                SUBTYPE { name = C }");
            Check("currentSubtype wins over priority",
                B9Config.ReadSwitch(byCurrent.GetNode("MODULE"), 0).DefaultIndex == 2);

            FakeNode noPriority = PartWithModule(@"
                name = ModuleB9PartSwitch
                SUBTYPE { name = A }
                SUBTYPE { name = B }");
            Check("first subtype is the default without hints",
                B9Config.ReadSwitch(noPriority.GetNode("MODULE"), 0).DefaultIndex == 0);
        }

        private static void ReadsTextures()
        {
            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                SUBTYPE
                {
                    name = Grey
                    transform = hull
                    TEXTURE
                    {
                        transform = hull
                        texture = ReStock/Parts/FuelTank/grey
                        currentTexture = Squad/Parts/FuelTank/orange
                        shaderProperty = _MainTex
                    }
                    TEXTURE
                    {
                        transform = hull
                        isNormalMap = true
                        _BumpMap = ReStock/Parts/FuelTank/grey_n
                    }
                }
                SUBTYPE
                {
                    name = Orange
                    transform = hull
                }");

            B9Switch b9 = B9Config.ReadSwitch(part.GetNode("MODULE"), 0);
            Check("textures are read", b9 != null && b9.Subtypes[0].Textures.Count == 2);
            if (b9 == null || b9.Subtypes[0].Textures.Count != 2)
            {
                return;
            }

            B9TextureSwitch main = b9.Subtypes[0].Textures[0];
            Check("texture path is read", main.Texture == "ReStock/Parts/FuelTank/grey");
            Check("current texture is read", main.CurrentTexture == "Squad/Parts/FuelTank/orange");
            Check("shader property is read", main.ShaderProperty == "_MainTex");
            Check("texture transform is read", main.Transforms.Count == 1 && main.Transforms[0] == "hull");

            B9TextureSwitch normal = b9.Subtypes[0].Textures[1];
            Check("shader-key shorthand is read", normal.Texture == "ReStock/Parts/FuelTank/grey_n" &&
                                                   normal.ShaderProperty == "_BumpMap");
            Check("normal map is flagged", normal.IsNormalMap);

            Check("shader keys are recognised", B9Config.IsShaderTextureKey("_MainTex") &&
                                                 B9Config.IsShaderTextureKey("_BumpMap") &&
                                                 !B9Config.IsShaderTextureKey("texture"));
        }

        private static void ReadsColorsAndAddedMass()
        {
            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                moduleID = Heatshield
                switcherDescription = Hull Style
                baseVolume = 400
                SUBTYPE
                {
                    name = Default
                    primaryColor = White
                    secondaryColor = Grey
                    tankType = Structural
                }
                SUBTYPE
                {
                    name = Super
                    primaryColor = White
                    secondaryColor = Auburn
                    addedMass = 0.4
                    tankType = OPTAB
                }");

            B9Switch b9 = B9Config.ReadSwitch(part.GetNode("MODULE"), 0);
            Check("colours are read", b9 != null && b9.Subtypes[1].PrimaryColor == "White" &&
                                      b9.Subtypes[1].SecondaryColor == "Auburn");
            Check("a colour-only switch still has something to switch", b9 != null && b9.HasAnythingToSwitch);
            Check("addedMass is read", b9 != null && Near(b9.Subtypes[1].AddedMass ?? 0, 0.4));
            Check("mass stays empty when only addedMass is given", b9 != null && !b9.Subtypes[1].Mass.HasValue);
        }

        private static void ReadsStockPartVariants()
        {
            FakeNode part = new FakeNode("PART").With("name", "grey-tank");
            part.AddNode(ParseBlock("MODULE", @"
                name = ModulePartVariants
                baseDisplayName = #autoLOC_8007118
                primaryColor = #fccb0a
                VARIANT
                {
                    name = Silver
                    displayName = Silver Style
                    themeName = Silver
                    primaryColor = #cecece
                    TEXTURE
                    {
                        materialName = Tank
                        mainTextureURL = Squad/Parts/FuelTank/Tanks_silver
                    }
                }
                VARIANT
                {
                    name = Dark
                    displayName = Dark Style
                    secondaryColor = Grey
                }"));

            IB9Node module = part.GetNode("MODULE");
            Check("ModulePartVariants is recognised", PartVariantsConfig.IsVariants(module));

            PartVariantSwitch variants = PartVariantsConfig.Read(module);
            Check("stock variants are read", variants != null && variants.Variants.Count == 2);
            if (variants == null || variants.Variants.Count != 2)
            {
                return;
            }

            Check("base colours are read", variants.PrimaryColor == "#fccb0a");
            Check("variant names and titles are read",
                variants.Variants[0].Name == "Silver" && variants.Variants[0].Title == "Silver Style");
            Check("variant colours are read", variants.Variants[0].PrimaryColor == "#cecece" &&
                                               variants.Variants[1].SecondaryColor == "Grey");
            Check("mainTextureURL becomes a main-texture swap",
                variants.Variants[0].Textures.Count == 1 &&
                variants.Variants[0].Textures[0].Texture == "Squad/Parts/FuelTank/Tanks_silver" &&
                variants.Variants[0].Textures[0].ShaderProperty == "_MainTex");

            Check("a module without variants is not a switch",
                PartVariantsConfig.Read(new FakeNode("MODULE")
                    .With("name", "ModulePartVariants")) == null);
        }

        private static void ReadsStockVariantGameObjects()
        {
            FakeNode part = new FakeNode("PART").With("name", "tiny-tank");
            part.AddNode(ParseBlock("MODULE", @"
                name = ModulePartVariants
                useMultipleDragCubes = false
                baseVariant = White
                VARIANT
                {
                    name = White
                    primaryColor = #ffffff
                    secondaryColor = #000000
                    GAMEOBJECTS
                    {
                        Tank1875Tiny = true
                        Tank1875TinyGrey = false
                        Tank1875TinyOrange = false
                    }
                }
                VARIANT
                {
                    name = Grey
                    primaryColor = #ffffff
                    GAMEOBJECTS
                    {
                        Tank1875Tiny = false
                        Tank1875TinyGrey = true
                    }
                }"));

            PartVariantSwitch variants = PartVariantsConfig.Read(part.GetNode("MODULE"));
            Check("baseVariant is read", variants != null && variants.BaseVariant == "White");
            Check("GAMEOBJECTS are read", variants != null &&
                                           variants.Variants[0].GameObjects.Count == 3 &&
                                           variants.Variants[1].GameObjects.Count == 2);
            if (variants == null || variants.Variants[0].GameObjects.Count != 3)
            {
                return;
            }

            Check("a style mesh is flagged visible",
                variants.Variants[0].GameObjects[0].Key == "Tank1875Tiny" && variants.Variants[0].GameObjects[0].Value);
            Check("the other style meshes are flagged hidden",
                !variants.Variants[0].GameObjects[1].Value && !variants.Variants[0].GameObjects[2].Value);
            Check("the second variant shows its own mesh", variants.Variants[1].GameObjects[1].Value);
        }

        private static void ReadsDisableTransforms()
        {
            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                SUBTYPE { name = A; transform = a }
                SUBTYPE { name = B; transform = b }");
            part.AddNode(new FakeNode("MODULE")
                .With("name", "ModuleB9DisableTransform")
                .With("transform", "alwaysHidden")
                .With("transform", "alsoHidden"));

            List<string> disabled = B9Config.ReadDisableTransforms(part);
            Check("ModuleB9DisableTransform is read", disabled.Count == 2 &&
                                                       disabled[0] == "alwaysHidden" &&
                                                       disabled[1] == "alsoHidden");
        }

        private static void KnowsWhenThereIsNothingToSwitch()
        {
            Check("one subtype is not a switch",
                !B9Config.ReadSwitch(PartWithModule(@"
                    name = ModuleB9PartSwitch
                    SUBTYPE { name = Only; tankType = LFO }").GetNode("MODULE"), 0).HasAnythingToSwitch);

            Check("ModuleB9DisableTransform is not a switch",
                !B9Config.IsSwitch(new FakeNode("MODULE").With("name", "ModuleB9DisableTransform")));
        }

        private static void ResolvesTankResources()
        {
            var tanks = new FakeTankLookup();
            tanks.Add("LFO", ("LiquidFuel", 0.45), ("Oxidizer", 0.55));
            tanks.Add("Structural", new (string, double)[0]);

            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                baseVolume = 800
                SUBTYPE { name = LFO; tankType = LFO }
                SUBTYPE { name = Empty; tankType = Structural }
                SUBTYPE
                {
                    name = Custom
                    RESOURCE { name = MonoPropellant; unitsPerVolume = 2 }
                }");

            B9Switch b9 = B9Config.ReadSwitch(part.GetNode("MODULE"), 0);
            var resolved = new List<B9ResolvedResource>();

            B9Config.ResolveResources(b9, b9.Subtypes[0], tanks, resolved);
            Check("tank type resources are resolved", resolved.Count == 2);
            Check("unitsPerVolume scales with base volume",
                resolved.Count == 2 && Near(resolved[0].MaxAmount, 0.45 * 800) && Near(resolved[1].MaxAmount, 0.55 * 800));
            Check("full by default", resolved.Count == 2 && Near(resolved[0].Amount, resolved[0].MaxAmount));

            resolved.Clear();
            B9Config.ResolveResources(b9, b9.Subtypes[1], tanks, resolved);
            Check("a structural subtype adds nothing", resolved.Count == 0);

            resolved.Clear();
            B9Config.ResolveResources(b9, b9.Subtypes[2], tanks, resolved);
            Check("a subtype's own RESOURCE block is used", resolved.Count == 1 &&
                                                             resolved[0].Name == "MonoPropellant" &&
                                                             Near(resolved[0].MaxAmount, 1600));
        }

        private static void ResolvesPercentFilled()
        {
            var tanks = new FakeTankLookup();
            tanks.Add("LFO", ("LiquidFuel", 1.0));

            FakeNode part = PartWithModule(@"
                name = ModuleB9PartSwitch
                baseVolume = 100
                SUBTYPE { name = Half; tankType = LFO; percentFilled = 0.5 }
                SUBTYPE { name = Quarter; tankType = LFO; percentFilled = 25 }");

            B9Switch b9 = B9Config.ReadSwitch(part.GetNode("MODULE"), 0);
            var resolved = new List<B9ResolvedResource>();

            B9Config.ResolveResources(b9, b9.Subtypes[0], tanks, resolved);
            Check("0..1 percent is applied", resolved.Count == 1 && Near(resolved[0].Amount, 50) &&
                                             Near(resolved[0].MaxAmount, 100));

            resolved.Clear();
            B9Config.ResolveResources(b9, b9.Subtypes[1], tanks, resolved);
            Check("0..100 percent is applied", resolved.Count == 1 && Near(resolved[0].Amount, 25));
        }

        private static void HandlesBrokenInput()
        {
            Check("null module is ignored", B9Config.ReadSwitch(null, 0) == null);
            Check("empty switch is ignored",
                B9Config.ReadSwitch(new FakeNode("MODULE").With("name", "ModuleB9PartSwitch"), 0) == null);
            Check("missing part is ignored", B9Config.ReadDisableTransforms(null).Count == 0);

            double x;
            double y;
            double z;
            Check("valid vector parses", B9Config.TryParseVector3("0, 1.5, -2", out x, out y, out z) &&
                                         Near(x, 0) && Near(y, 1.5) && Near(z, -2));
            Check("short vector is rejected", !B9Config.TryParseVector3("0, 1", out x, out y, out z));

            FakeNode withNode = PartWithModule(@"
                name = ModuleB9PartSwitch
                baseVolume = 100
                SUBTYPE
                {
                    name = A
                    node = top
                    NODE { name = top; position = 0, 1.25, 0 }
                }
                SUBTYPE { name = B }");
            B9Switch b9 = B9Config.ReadSwitch(withNode.GetNode("MODULE"), 0);
            Check("NODE positions are read", b9 != null && b9.Subtypes[0].Nodes.Count == 1 &&
                                              b9.Subtypes[0].Nodes[0].Name == "top" &&
                                              Near(b9.Subtypes[0].Nodes[0].Y, 1.25));
        }

        // ------------------------------------------------------------------ real configs

        private static void RealPartConfigs()
        {
            Console.WriteLine();
            Console.WriteLine("=== real configs in " + Ksp1Root);
            if (!Directory.Exists(Ksp1Root))
            {
                Check("KSP1 folder exists", false);
                return;
            }

            int files = 0;
            int parts = 0;
            int switches = 0;
            int withTransforms = 0;
            int withTextures = 0;
            int withTankTypes = 0;
            int withVisuals = 0;
            int tankTypeDefinitions = 0;
            var examples = new List<string>();

            foreach (string file in EnumerateConfigs())
            {
                files++;
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception)
                {
                    continue;
                }

                // Only the files that mention B9 can hold a switch; this keeps the scan quick.
                if (text.IndexOf("ModuleB9PartSwitch", StringComparison.OrdinalIgnoreCase) < 0 &&
                    text.IndexOf("B9_TANK_TYPE", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                Ksp1ConfigNode root;
                try
                {
                    root = Ksp1ConfigNode.Parse(text);
                }
                catch (Exception)
                {
                    continue;
                }

                if (root == null)
                {
                    continue;
                }

                tankTypeDefinitions += CountTankTypes(root);

                foreach (IB9Node module in SwitchModules(root))
                {
                    parts++;
                    int moduleIndex = 0;

                    B9Switch b9 = B9Config.ReadSwitch(module, moduleIndex);
                    if (b9 == null)
                    {
                        continue;
                    }

                    switches++;
                    bool transforms = b9.Subtypes.Any(s => s.Transforms.Count > 0);
                    bool textures = b9.Subtypes.Any(s => s.Textures.Count > 0);
                    bool tanks = b9.Subtypes.Any(s => !string.IsNullOrEmpty(s.TankType));

                    if (transforms || textures)
                    {
                        withVisuals++;
                    }

                    if (transforms)
                    {
                        withTransforms++;
                    }

                    if (textures)
                    {
                        withTextures++;
                    }

                    if (tanks)
                    {
                        withTankTypes++;
                    }

                    if (examples.Count < 8 && (transforms || tanks))
                    {
                        examples.Add("  " + Path.GetFileName(file) + " / " + b9.ModuleId + " -> " +
                                     string.Join(", ", b9.Subtypes.Select(s => s.Name).ToArray()) +
                                     (transforms ? "  [mesh]" : "") + (tanks ? "  [tank]" : ""));
                    }
                }
            }

            Console.WriteLine("  files with B9: " + files + ", part nodes: " + parts + ", B9 switches: " + switches);
            Console.WriteLine("  with meshes: " + withTransforms + ", with textures: " + withTextures +
                              ", with visuals: " + withVisuals + ", with tank types: " + withTankTypes);
            Console.WriteLine("  B9_TANK_TYPE definitions: " + tankTypeDefinitions);
            foreach (string example in examples)
            {
                Console.WriteLine(example);
            }

            Check("B9 config files were found", files > 0);
            Check("real B9 switches are parsed", switches > 0);
            Check("real switchable meshes are found", withTransforms > 0);
            Check("real tank types are defined", tankTypeDefinitions > 0);
        }

        /// <summary>Every <c>ModuleB9PartSwitch</c> anywhere in a config file, including ModuleManager patches.</summary>
        private static List<IB9Node> SwitchModules(Ksp1ConfigNode root)
        {
            var found = new List<IB9Node>();
            Walk(Ksp1NodeAdapter.Wrap(root), found, 0);
            return found;
        }

        private static void Walk(IB9Node node, List<IB9Node> found, int depth)
        {
            if (node == null || depth > 12)
            {
                return;
            }

            if (B9Config.IsSwitch(node))
            {
                found.Add(node);
            }

            foreach (IB9Node child in node.Nodes)
            {
                Walk(child, found, depth + 1);
            }
        }

        /// <summary>Part nodes of a config file, including the ModuleManager spellings.</summary>
        private static IEnumerable<Ksp1ConfigNode> PartNodes(Ksp1ConfigNode node)
        {
            if (node == null)
            {
                yield break;
            }

            string name = node.Name ?? "";
            if (name.Equals("PART", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("@PART", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("+PART", StringComparison.OrdinalIgnoreCase))
            {
                yield return node;
            }

            foreach (Ksp1ConfigNode child in AllNodes(node))
            {
                foreach (Ksp1ConfigNode found in PartNodes(child))
                {
                    yield return found;
                }
            }
        }

        private static int CountTankTypes(Ksp1ConfigNode node)
        {
            int count = 0;
            foreach (Ksp1ConfigNode child in AllNodes(node))
            {
                if (B9Config.NameMatches(child.Name, "B9_TANK_TYPE"))
                {
                    count++;
                }
            }

            return count;
        }

        private static IEnumerable<Ksp1ConfigNode> AllNodes(Ksp1ConfigNode node)
        {
            return node == null || node.Nodes == null ? new Ksp1ConfigNode[0] : node.Nodes;
        }

        private static IEnumerable<string> EnumerateConfigs()
        {
            int limit = 4000;
            int seen = 0;
            foreach (string file in Directory.EnumerateFiles(Ksp1Root, "*.cfg", SearchOption.AllDirectories))
            {
                if (seen++ >= limit)
                {
                    yield break;
                }

                yield return file;
            }
        }
        // ------------------------------------------------------------------ helpers

        private static FakeNode PartWithModule(string moduleBody)
        {
            var part = new FakeNode("PART").With("name", "test-part");
            part.AddNode(ParseBlock("MODULE", moduleBody));
            return part;
        }

        /// <summary>Tiny node parser for the synthetic configs (the real ones go through Ksp1ConfigNode).</summary>
        private static FakeNode ParseBlock(string name, string body)
        {
            var root = new FakeNode(name);
            int index = 0;
            ParseNodes(body, ref index, root, true);
            return root;
        }

        private static void ParseNodes(string text, ref int index, FakeNode parent, bool stopAtBrace)
        {
            while (index < text.Length)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return;
                }

                char c = text[index];
                if (c == '}')
                {
                    index++;
                    if (stopAtBrace)
                    {
                        return;
                    }

                    continue;
                }

                if (c == ';')
                {
                    index++;
                    continue;
                }

                int start = index;
                while (index < text.Length && "={;\n\r}".IndexOf(text[index]) < 0)
                {
                    index++;
                }

                string token = text.Substring(start, index - start).Trim();
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return;
                }

                if (text[index] == '{')
                {
                    index++;
                    var child = new FakeNode(token);
                    parent.AddNode(child);
                    ParseNodes(text, ref index, child, true);
                    continue;
                }

                if (text[index] == '=')
                {
                    index++;
                    int valueStart = index;
                    while (index < text.Length && text[index] != ';' && text[index] != '\n' &&
                           text[index] != '\r' && text[index] != '}' && text[index] != '{')
                    {
                        index++;
                    }

                    string value = text.Substring(valueStart, index - valueStart).Trim();
                    if (token.Length > 0)
                    {
                        parent.With(token, value);
                    }

                    continue;
                }

                index++;
            }
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static bool Near(double actual, double expected)
        {
            return Math.Abs(actual - expected) < 1e-3;
        }

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                Console.WriteLine("  ok   " + name);
                return;
            }

            _failures++;
            Console.WriteLine("  FAIL " + name);
        }

        private sealed class FakeTankLookup : IB9TankTypeLookup
        {
            private readonly Dictionary<string, B9TankType> _types =
                new Dictionary<string, B9TankType>(StringComparer.OrdinalIgnoreCase);

            public void Add(string name, params (string resource, double unitsPerVolume)[] resources)
            {
                var type = new B9TankType { Name = name };
                for (int i = 0; i < resources.Length; i++)
                {
                    type.Resources.Add(new B9ResourceSpec
                    {
                        Name = resources[i].resource,
                        UnitsPerVolume = resources[i].unitsPerVolume
                    });
                }

                _types[name] = type;
            }

            public B9TankType Find(string name)
            {
                B9TankType type;
                return name != null && _types.TryGetValue(name, out type) ? type : null;
            }
        }

        private sealed class FakeNode : IB9Node
        {
            private readonly List<KeyValuePair<string, string>> _values = new List<KeyValuePair<string, string>>();
            private readonly List<IB9Node> _nodes = new List<IB9Node>();

            public FakeNode(string name)
            {
                Name = name;
            }

            public string Name { get; private set; }

            public IEnumerable<KeyValuePair<string, string>> Values
            {
                get { return _values; }
            }

            public IEnumerable<IB9Node> Nodes
            {
                get { return _nodes; }
            }

            public FakeNode With(string key, string value)
            {
                _values.Add(new KeyValuePair<string, string>(key, value));
                return this;
            }

            public FakeNode AddNode(FakeNode node)
            {
                _nodes.Add(node);
                return this;
            }

            public string GetValue(string name, string fallback = null)
            {
                for (int i = _values.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_values[i].Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return _values[i].Value;
                    }
                }

                return fallback;
            }

            public IEnumerable<string> GetValues(string name)
            {
                return _values.Where(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase))
                              .Select(v => v.Value);
            }

            public IEnumerable<IB9Node> GetNodes(string name)
            {
                return _nodes.Where(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            public FakeNode GetNode(string name)
            {
                return GetNodes(name).FirstOrDefault() as FakeNode;
            }
        }
    }
}
