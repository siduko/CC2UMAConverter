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
            throw new NotImplementedException();
        }

        public static IReadOnlyList<string> GetAliasesForChannel(string channelName)
        {
            throw new NotImplementedException();
        }

        public static bool ShouldUseTransparentRendering(string overlayName, bool hasTransparencyTexture)
        {
            throw new NotImplementedException();
        }
    }
}
