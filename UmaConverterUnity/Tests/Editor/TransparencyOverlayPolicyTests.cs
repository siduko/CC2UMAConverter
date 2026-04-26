using NUnit.Framework;
using UMAConverter.Editor.Transparency;

namespace UMAConverter.Tests.Editor
{
    public class TransparencyOverlayPolicyTests
    {
        [TestCase("TransparencyMap", true)]
        [TestCase("Opacity", true)]
        [TestCase("Alpha", true)]
        [TestCase("OpacityMask", true)]
        [TestCase("SpecGlossMap", false)]
        public void IsTransparencyAlias_ReturnsExpectedResult(string candidate, bool expected)
        {
            Assert.That(TransparencyOverlayPolicy.IsTransparencyAlias(candidate), Is.EqualTo(expected));
        }

        [TestCase("Eyelashes", true)]
        [TestCase("Cornea", true)]
        [TestCase("EyeMoisture", true)]
        [TestCase("GlassLens", true)]
        [TestCase("Body", false)]
        [TestCase("Torso", false)]
        public void ShouldUseTransparentRendering_RequiresKnownOverlayName(string overlayName, bool expected)
        {
            Assert.That(
                TransparencyOverlayPolicy.ShouldUseTransparentRendering(overlayName, hasTransparencyTexture: true),
                Is.EqualTo(expected));
        }

        [Test]
        public void ShouldUseTransparentRendering_StaysOpaque_WhenTransparencyTextureIsMissing()
        {
            Assert.That(
                TransparencyOverlayPolicy.ShouldUseTransparentRendering("Eyelashes", hasTransparencyTexture: false),
                Is.False);
        }

        [TestCase("TransparencyMap", new[] { "TransparencyMap", "Opacity", "Alpha", "OpacityMask" })]
        [TestCase("SpecGlossMap", new[] { "SpecGlossMap", "Specular", "SpecGloss", "SpecularGloss" })]
        public void GetTextureCandidatesForChannelStyleAliases_ReturnInStableOrder(string channelName, string[] expected)
        {
            CollectionAssert.AreEqual(expected, TransparencyOverlayPolicy.GetAliasesForChannel(channelName));
        }
    }
}
