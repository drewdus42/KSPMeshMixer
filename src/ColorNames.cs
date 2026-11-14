using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KSPMeshMixer
{
    /// <summary>
    /// KSP 1 mods name colours instead of writing hex ("primaryColor = Auburn", "secondaryColor = Grey").
    /// This maps the names that show up in part configs (B9's XKCD-style palette plus the plain ones)
    /// and falls back to hex parsing.
    /// </summary>
    internal static class ColorNames
    {
        private static readonly Dictionary<string, string> Named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "white", "#FFFFFF" },
            { "offwhite", "#FFFFE4" },
            { "black", "#000000" },
            { "dark", "#1A1A1A" },
            { "grey", "#808080" },
            { "gray", "#808080" },
            { "lightgrey", "#D3D3D3" },
            { "lightgray", "#D3D3D3" },
            { "darkgrey", "#3F3F3F" },
            { "darkgray", "#3F3F3F" },
            { "silver", "#C0C0C0" },
            { "gold", "#FFD700" },
            { "bronze", "#CD7F32" },
            { "copper", "#B87333" },
            { "brass", "#B5A642" },
            { "red", "#FF0000" },
            { "darkred", "#8B0000" },
            { "crimson", "#DC143C" },
            { "maroon", "#800000" },
            { "orange", "#FFA500" },
            { "darkorange", "#FF8C00" },
            { "yellow", "#FFFF00" },
            { "green", "#008000" },
            { "darkgreen", "#006400" },
            { "lime", "#00FF00" },
            { "olive", "#808000" },
            { "blue", "#0000FF" },
            { "darkblue", "#00008B" },
            { "navy", "#000080" },
            { "skyblue", "#87CEEB" },
            { "teal", "#008080" },
            { "cyan", "#00FFFF" },
            { "turquoise", "#40E0D0" },
            { "purple", "#800080" },
            { "violet", "#EE82EE" },
            { "magenta", "#FF00FF" },
            { "pink", "#FFC0CB" },
            { "brown", "#A52A2A" },
            { "tan", "#D2B48C" },
            { "beige", "#F5F5DC" },
            { "auburn", "#A52A2A" },
            { "charcoal", "#36454F" },
            { "cream", "#FFFDD0" },
            { "ivory", "#FFFFF0" },
            { "khaki", "#F0E68C" }
        };

        /// <summary>Parses a hex colour or a name; returns false for anything unrecognised.</summary>
        public static bool TryParse(string raw, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            string text = raw.Trim();
            string hex;
            if (Named.TryGetValue(text, out hex))
            {
                return TryParseHex(hex, out color);
            }

            return TryParseHex(text, out color);
        }

        /// <summary>
        /// Pure hex parsing: <c>#rgb</c>, <c>#rrggbb</c> or <c>#aarrggbb</c>, with or without the hash.
        /// Unity's parser is avoided on purpose so this works (and is tested) outside the game.
        /// </summary>
        private static bool TryParseHex(string text, out Color color)
        {
            color = Color.white;
            string hex = text.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }

            if (hex.Length == 3)
            {
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }

            if (hex.Length != 6 && hex.Length != 8)
            {
                return false;
            }

            int offset = hex.Length == 8 ? 2 : 0;
            int r;
            int g;
            int b;
            if (!TryParseByte(hex, offset, out r) || !TryParseByte(hex, offset + 2, out g) ||
                !TryParseByte(hex, offset + 4, out b))
            {
                return false;
            }

            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

        private static bool TryParseByte(string hex, int index, out int value)
        {
            value = 0;
            if (index + 1 >= hex.Length)
            {
                return false;
            }

            int high = HexDigit(hex[index]);
            int low = HexDigit(hex[index + 1]);
            if (high < 0 || low < 0)
            {
                return false;
            }

            value = high * 16 + low;
            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            return -1;
        }

        /// <summary>Round-trips a colour through hex, which is how the override data stores it.</summary>
        public static string ToHex(Color color)
        {
            return "#" + Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f).ToString("X2", CultureInfo.InvariantCulture) +
                   Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f).ToString("X2", CultureInfo.InvariantCulture) +
                   Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f).ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
