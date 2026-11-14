using System;
using System.Collections.Generic;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;

namespace KSPMeshMixer
{
    /// <summary>
    /// Every <c>B9_TANK_TYPE</c> the loaded mods define, so a subtype's <c>tankType</c> can be turned
    /// into real resource amounts. Built once from the importer's own finalized config database (which
    /// is already parsed and cached by the time parts import), with the common B9 tank types as a
    /// fallback for parts that rely on the built-in names.
    /// </summary>
    internal sealed class TankCatalog : IB9TankTypeLookup
    {
        private static TankCatalog _instance;
        private static bool _buildAttempted;

        private readonly Dictionary<string, B9TankType> _tankTypes =
            new Dictionary<string, B9TankType>(StringComparer.OrdinalIgnoreCase);

        public static TankCatalog Instance
        {
            get
            {
                if (_instance == null && !_buildAttempted)
                {
                    _buildAttempted = true;
                    _instance = Build();
                }

                return _instance;
            }
        }

        public B9TankType Find(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            B9TankType tankType;
            return _tankTypes.TryGetValue(name.Trim(), out tankType) ? tankType : null;
        }

        public int Count
        {
            get { return _tankTypes.Count; }
        }

        private static TankCatalog Build()
        {
            var catalog = new TankCatalog();
            try
            {
                Ksp1FinalizedConfigDatabase database = Ksp1FinalizedConfigDatabase.Get(new Ksp1ImportReport());
                if (database != null && database.Files != null)
                {
                    foreach (Ksp1ConfigFile file in database.Files)
                    {
                        if (file != null && file.Node != null)
                        {
                            catalog.AddFrom(file.Node);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MeshMixerLog.Warn("Could not read the B9 tank types; using the built-in names only: " + ex.Message);
            }

            catalog.AddBuiltIn("LFO", ("LiquidFuel", 0.45, 1.0), ("Oxidizer", 0.55, 1.0));
            catalog.AddBuiltIn("LF", ("LiquidFuel", 1.0, 1.0));
            catalog.AddBuiltIn("LiquidFuel", ("LiquidFuel", 1.0, 1.0));
            catalog.AddBuiltIn("OX", ("Oxidizer", 1.0, 1.0));
            catalog.AddBuiltIn("Oxidizer", ("Oxidizer", 1.0, 1.0));
            catalog.AddBuiltIn("LM", ("LqdMethane", 1.0, 1.0));
            catalog.AddBuiltIn("LqdMethane", ("LqdMethane", 1.0, 1.0));
            catalog.AddBuiltIn("LMOx", ("LqdMethane", 1.875, 1.0), ("LqdOxygen", 0.625, 1.0));
            return catalog;
        }

        private void AddFrom(Ksp1ConfigNode root)
        {
            foreach (Ksp1ConfigNode node in root.Nodes)
            {
                if (node == null || !B9Config.NameMatches(node.Name, "B9_TANK_TYPE"))
                {
                    continue;
                }

                string name = node.GetValue("name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var tankType = new B9TankType
                {
                    Name = name.Trim(),
                    PercentFilled = B9Config.ReadDouble(node.GetValue("percentFilled"))
                };

                foreach (Ksp1ConfigNode resourceNode in node.GetNodes("RESOURCE"))
                {
                    string resourceName = resourceNode.GetValue("name");
                    if (string.IsNullOrEmpty(resourceName))
                    {
                        continue;
                    }

                    tankType.Resources.Add(new B9ResourceSpec
                    {
                        Name = resourceName.Trim(),
                        Amount = B9Config.ReadDouble(resourceNode.GetValue("amount")),
                        MaxAmount = B9Config.ReadDouble(resourceNode.GetValue("maxAmount")),
                        UnitsPerVolume = B9Config.ReadDouble(resourceNode.GetValue("unitsPerVolume")),
                        PercentFilled = B9Config.ReadDouble(resourceNode.GetValue("percentFilled"))
                    });
                }

                if (tankType.Resources.Count > 0)
                {
                    _tankTypes[tankType.Name] = tankType;
                }
            }
        }

        private void AddBuiltIn(string name, params (string resource, double unitsPerVolume, double percentFilled)[] resources)
        {
            if (_tankTypes.ContainsKey(name))
            {
                return;
            }

            var tankType = new B9TankType { Name = name };
            for (int i = 0; i < resources.Length; i++)
            {
                tankType.Resources.Add(new B9ResourceSpec
                {
                    Name = resources[i].resource,
                    UnitsPerVolume = resources[i].unitsPerVolume,
                    PercentFilled = resources[i].percentFilled
                });
            }

            _tankTypes[name] = tankType;
        }
    }
}
