using System;
using System.Collections.Generic;

namespace UMAConverter.Editor.Transparency
{
    internal static class TransparencyOverlayPolicy
    {
        private static readonly string[] TransparencyAliases =
        {
            "TransparencyMap",
            "Opacity",
            "Alpha",
            "OpacityMask"
        };

        private static readonly string[] TransparentOverlayKeywords =
        {
            "lash",
            "eyelash",
            "cornea",
            "moisture",
            "tear",
            "glass",
            "lens"
        };

        public static bool IsTransparencyAlias(string candidate)
        {
            foreach (var alias in TransparencyAliases)
            {
                if (alias == candidate)
                {
                    return true;
                }
            }
            return false;
        }

        public static IReadOnlyList<string> GetAliasesForChannel(string channelName)
        {
            if (channelName == "TransparencyMap")
            {
                return new[] { "TransparencyMap", "Opacity", "Alpha", "OpacityMask" };
            }
            if (channelName == "SpecGlossMap")
            {
                return new[] { "SpecGlossMap", "Specular", "SpecGloss", "SpecularGloss" };
            }
            return new string[0];
        }

        public static bool ShouldUseTransparentRendering(string overlayName, bool hasTransparencyTexture)
        {
            if (!hasTransparencyTexture)
            {
                return false;
            }

            var lowerOverlayName = overlayName.ToLower();
            foreach (var keyword in TransparentOverlayKeywords)
            {
                if (lowerOverlayName.Contains(keyword))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
