using System;
using System.IO;
using Newtonsoft.Json;

namespace KSPMeshMixer
{
    /// <summary>
    /// Settings live in a JSON file next to the dll. Deliberately simple so a broken file can never stop
    /// the mod: anything unreadable falls back to the defaults.
    /// </summary>
    internal sealed class MeshMixerSettings
    {
        public const string FileName = "KSPMeshMixer-config.json";

        private static MeshMixerSettings _current;

        public static MeshMixerSettings Current
        {
            get { return _current ?? (_current = new MeshMixerSettings()); }
        }

        /// <summary>Master switch. Off hands every part back to the importer's own B9 handling.</summary>
        public bool Enabled = true;

        /// <summary>
        /// Apply a subtype's <c>TEXTURE</c> blocks (ReStock/`_MainTex` restyles).
        /// </summary>
        public bool SwitchTextures = true;

        /// <summary>Apply a subtype's <c>primaryColor</c>/<c>secondaryColor</c> (KSP 2 paint + material tint).</summary>
        public bool SwitchColors = true;

        /// <summary>
        /// Imported KSP 1 materials do not understand KSP 2's paint slots (<c>_PaintA</c>/<c>_PaintB</c>),
        /// so a colour switch can also tint <c>_Color</c> the way KSP 1's shader did. Off by default:
        /// KSP 1 variant colours are often just a shade (an orange style's <c>#f49841</c>) and tinting a
        /// texture that is already orange leaves a visible double-orange cast. Turn on only for parts
        /// whose materials really are neutral.
        /// </summary>
        public bool TintMaterials = false;

        /// <summary>Swap the tank contents when a subtype names a tank type.</summary>
        public bool SwitchResources = true;

        /// <summary>Apply a subtype's <c>mass</c> as a delta against the part's base mass.</summary>
        public bool SwitchMass = true;

        /// <summary>Log every converted part, not just the ones with problems.</summary>
        public bool VerboseLogging;

        public static MeshMixerSettings Load(string folder)
        {
            var settings = new MeshMixerSettings();
            if (string.IsNullOrEmpty(folder))
            {
                _current = settings;
                return settings;
            }

            string path = Path.Combine(folder, FileName);
            try
            {
                if (File.Exists(path))
                {
                    MeshMixerSettings loaded = JsonConvert.DeserializeObject<MeshMixerSettings>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        settings = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                MeshMixerLog.Warn("Could not read " + FileName + "; using defaults: " + ex.Message);
            }

            _current = settings;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MeshMixerLog.Warn("Could not write " + FileName + ": " + ex.Message);
            }

            return settings;
        }
    }
}
