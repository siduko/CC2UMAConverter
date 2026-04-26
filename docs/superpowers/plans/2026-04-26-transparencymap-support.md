# TransparencyMap Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add TransparencyMap support to the Unity UMA converter so known transparent overlays can resolve opacity textures and render through an alpha-blend path without changing the opaque default for all other overlays.

**Architecture:** Keep the texture lookup flow in `UMAConverter.cs`, but extract transparency classification into a small testable Editor helper. Extend the UMAMaterial asset contract with a `_TransparencyMap` channel, add a dedicated transparent companion UMAMaterial plus material asset, and update the shader graph so transparent overlays can switch to that material while unknown overlays keep using the existing opaque default.

**Tech Stack:** Unity 2022.3 package assets, C# Editor scripts, Unity EditMode tests, YAML/serialized Unity assets, Shader Graph

---

### Task 1: Create A Testable Transparency Policy Surface

**Files:**
- Create: `UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs`
- Create: `UmaConverterUnity/Editor/UMAConverter.Editor.asmdef`
- Create: `UmaConverterUnity/Tests/Editor/UMAConverter.EditorTests.asmdef`
- Create: `UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs`
- Test: `UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs`

- [ ] **Step 1: Write the failing EditMode tests**

```csharp
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
```

- [ ] **Step 2: Add assembly definitions so Editor code can be tested**

`UmaConverterUnity/Editor/UMAConverter.Editor.asmdef`

```json
{
  "name": "UMAConverter.Editor",
  "rootNamespace": "UMAConverter",
  "references": [],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`UmaConverterUnity/Tests/Editor/UMAConverter.EditorTests.asmdef`

```json
{
  "name": "UMAConverter.EditorTests",
  "rootNamespace": "UMAConverter.Tests.Editor",
  "references": [
    "UMAConverter.Editor"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": false,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false,
  "optionalUnityReferences": [
    "TestAssemblies"
  ]
}
```

- [ ] **Step 3: Add the minimal helper implementation skeleton**

`UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs`

```csharp
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
```

- [ ] **Step 4: Run the EditMode tests to confirm they fail first**

Run in Unity Editor: `Window > General > Test Runner > EditMode > Run All`
Expected: tests fail with `NotImplementedException` from `TransparencyOverlayPolicy`

- [ ] **Step 5: Commit**

```bash
git add UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs UmaConverterUnity/Editor/UMAConverter.Editor.asmdef UmaConverterUnity/Tests/Editor/UMAConverter.EditorTests.asmdef UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs
git commit -m "test: add transparency policy test surface"
```

### Task 2: Implement Transparency Alias And Classification Logic

**Files:**
- Modify: `UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs`
- Test: `UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs`

- [ ] **Step 1: Implement the helper with the approved rules**

```csharp
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
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            foreach (string alias in TransparencyAliases)
            {
                if (string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static IReadOnlyList<string> GetAliasesForChannel(string channelName)
        {
            if (IsTransparencyAlias(channelName))
            {
                return TransparencyAliases;
            }

            if (string.Equals(channelName, "SpecGlossMap", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "SpecGlossMap", "Specular", "SpecGloss", "SpecularGloss" };
            }

            return new[] { channelName };
        }

        public static bool ShouldUseTransparentRendering(string overlayName, bool hasTransparencyTexture)
        {
            if (!hasTransparencyTexture || string.IsNullOrWhiteSpace(overlayName))
            {
                return false;
            }

            foreach (string keyword in TransparentOverlayKeywords)
            {
                if (overlayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 2: Run the EditMode tests to confirm the helper passes**

Run in Unity Editor: `Window > General > Test Runner > EditMode > Run All`
Expected: all `TransparencyOverlayPolicyTests` pass

- [ ] **Step 3: Commit**

```bash
git add UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs
git commit -m "feat: implement transparency overlay policy"
```

### Task 3: Wire TransparencyMap Into UMAConverter Texture Resolution

**Files:**
- Modify: `UmaConverterUnity/Editor/UMAConverter.cs:347-542`
- Modify: `UmaConverterUnity/Editor/UMAConverterSettings.cs:1-52`
- Test: `UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs`

- [ ] **Step 1: Update the channel alias table to include `_TransparencyMap` behavior**

Add this entry inside `textureChannelAliases` in `UmaConverterUnity/Editor/UMAConverter.cs`:

```csharp
{ "TransparencyMap", new List<string> { "Opacity", "Alpha", "OpacityMask", "TransparencyMap" } },
```

- [ ] **Step 2: Add a helper to find whether a resolved texture list contains transparency**

Insert this helper near `GetTextureCandidatesForChannel`:

```csharp
private bool HasTransparencyTexture(UMAMaterial umaMaterial, Texture[] textures)
{
    if (umaMaterial == null || textures == null)
    {
        return false;
    }

    for (int index = 0; index < umaMaterial.channels.Length && index < textures.Length; index++)
    {
        UMAMaterial.MaterialChannel channel = umaMaterial.channels[index];
        string channelName = channel.materialPropertyName.Replace("_", "");
        if (UMAConverter.Editor.Transparency.TransparencyOverlayPolicy.IsTransparencyAlias(channelName) && textures[index] != null)
        {
            return true;
        }
    }

    return false;
}
```

- [ ] **Step 3: Add a transparent companion material setting**

Update `UmaConverterUnity/Editor/UMAConverterSettings.cs` like this:

```csharp
public UMAMaterial defaultMaterial = null;
public UMAMaterial transparentMaterial = null;
public bool removeMeshAfterCreating = false;

public void OnEnable()
{
    if (defaultMaterial == null)
    {
        defaultMaterial = AssetDatabase.LoadAssetAtPath<UMAMaterial>("Packages/com.vwgamedev.umaconverter/Runtime/UMAMaterials/CCMaterial.asset");
    }

    if (transparentMaterial == null)
    {
        transparentMaterial = AssetDatabase.LoadAssetAtPath<UMAMaterial>("Packages/com.vwgamedev.umaconverter/Runtime/UMAMaterials/CCTransparentMaterial.asset");
    }
}
```

- [ ] **Step 4: Evaluate transparency in `ApplyOverlayData()` and switch overlay material when required**

Add this block immediately after `asset.textureList = hasSlotTextures ? slotMaterialTextures : defaultMaterialTextures;`:

```csharp
UMAMaterial resolvedMaterial = hasSlotTextures ? slotAsset.material : UMAConverterSettings.Instance.defaultMaterial;
bool hasTransparencyTexture = HasTransparencyTexture(resolvedMaterial, asset.textureList);
bool shouldUseTransparentRendering = UMAConverter.Editor.Transparency.TransparencyOverlayPolicy.ShouldUseTransparentRendering(
    textureOverlayName,
    hasTransparencyTexture);

if (shouldUseTransparentRendering && UMAConverterSettings.Instance.transparentMaterial != null)
{
    asset.material = UMAConverterSettings.Instance.transparentMaterial;
}
else
{
    asset.material = slotAsset.material;
}

Debug.Log(
    "[UMAConverter] Transparency evaluation: overlay='" + textureOverlayName +
    "', hasTransparencyTexture=" + hasTransparencyTexture +
    ", shouldUseTransparentRendering=" + shouldUseTransparentRendering +
    ", resolvedMaterial='" + asset.material.name + "'");
```

- [ ] **Step 5: Replace the old alias lookup helper with one that delegates transparency aliases to the new policy**

Update `GetTextureCandidatesForChannel` to:

```csharp
private List<string> GetTextureCandidatesForChannel(string channelName)
{
    List<string> candidates = new List<string>();

    foreach (string alias in UMAConverter.Editor.Transparency.TransparencyOverlayPolicy.GetAliasesForChannel(channelName))
    {
        if (!candidates.Contains(alias))
        {
            candidates.Add(alias);
        }
    }

    List<string> aliases;
    if (textureChannelAliases.TryGetValue(channelName, out aliases))
    {
        foreach (string alias in aliases)
        {
            if (!candidates.Contains(alias))
            {
                candidates.Add(alias);
            }
        }
    }

    if (candidates.Count == 0)
    {
        candidates.Add(channelName);
    }

    return candidates;
}
```

- [ ] **Step 6: Run the EditMode tests and grep checks for the new settings/material wiring**

Run in Unity Editor: `Window > General > Test Runner > EditMode > Run All`
Expected: `TransparencyOverlayPolicyTests` stay green

Run:

```bash
rg "TransparencyMap|HasTransparencyTexture|ShouldUseTransparentRendering|transparentMaterial" UmaConverterUnity/Editor/UMAConverter.cs UmaConverterUnity/Editor/UMAConverterSettings.cs
```

Expected: matches for the new alias entry, helper call, transparency evaluation block, and the `transparentMaterial` setting

- [ ] **Step 7: Commit**

```bash
git add UmaConverterUnity/Editor/UMAConverter.cs UmaConverterUnity/Editor/UMAConverterSettings.cs UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs
git commit -m "feat: resolve transparency textures in uma converter"
```

### Task 4: Extend The UMAMaterial Asset Contract

**Files:**
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset:20-77`
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat:26-129`
- Create: `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset`
- Create: `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat`

- [ ] **Step 1: Add a `_TransparencyMap` channel to the UMAMaterial asset**

Insert this block after the `_SpecGlossMap` channel entry in `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset`:

```yaml
  - channelType: 0
    textureFormat: 0
    materialPropertyName: _TransparencyMap
    sourceTextureName:
    Compression: 0
    DownSample: 0
    ConvertRenderTexture: 0
    NonShaderTexture: 0
```

- [ ] **Step 2: Add the serialized texture slot to the material asset**

Insert this block in `m_TexEnvs` in `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat`:

```yaml
    - _TransparencyMap:
        m_Texture: {fileID: 0}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
```

- [ ] **Step 3: Create a transparent companion UMAMaterial asset that points at a transparent material**

Create `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset` by duplicating `CCMaterial.asset` inside Unity so the `.asset` and `.mat` references are regenerated together, then change these serialized fields:

```yaml
    m_Name: CCTransparentMaterial
```

Keep the same channel list as `CCMaterial.asset`, including the new `_TransparencyMap` channel.

- [ ] **Step 4: Create a transparent companion material file**

Create `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat` by copying `CCMaterial.mat` and changing these serialized fields:

```yaml
    m_Name: CCTransparentMaterial
        - _Surface: 1
        - _SrcBlend: 5
        - _DstBlend: 10
        - _SrcBlendAlpha: 1
        - _DstBlendAlpha: 10
        - _ZWrite: 0
```

Also include the `_TransparencyMap` entry in `m_TexEnvs`.

- [ ] **Step 5: Verify the material assets contain the new property and companion assets**

Run:

```bash
rg "_TransparencyMap|CCTransparentMaterial|_Surface: 1" UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat
```

Expected: `_TransparencyMap` appears in both material contracts, and the transparent companion material serializes `_Surface: 1`

- [ ] **Step 6: Commit**

```bash
git add UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat
git commit -m "feat: add transparency map material assets"
```

### Task 5: Add Alpha-Blend Support To The Shader Graph

**Files:**
- Modify: `UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph`
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat`

- [ ] **Step 1: Add a `_TransparencyMap` texture property to the shader graph**

Create a new `Texture2DShaderProperty` in `CC4PBR.shadergraph` with this serialized shape:

```json
{
  "m_Type": "UnityEditor.ShaderGraph.Internal.Texture2DShaderProperty",
  "m_Name": "TransparencyMap",
  "m_DefaultReferenceName": "_TransparencyMap",
  "m_GeneratePropertyBlock": true,
  "m_Modifiable": true,
  "m_DefaultType": 1
}
```

- [ ] **Step 2: Connect the transparency texture alpha into the fragment output**

In Shader Graph Editor:

```text
Sample Texture 2D(_TransparencyMap) -> A channel -> Surface Alpha
Keep Alpha Clipping Off
Preserve the existing metallic/normal/emission wiring
```

- [ ] **Step 3: Keep the opaque default material stable while using the transparent companion material for opt-in overlays**

Ensure these values remain true after the graph reimport:

```yaml
CCMaterial.mat
    - _AlphaClip: 0
    - _Surface: 0

CCTransparentMaterial.mat
    - _AlphaClip: 0
    - _Surface: 1
```

This keeps the default material opaque while the converter routes known transparent overlays onto the transparent companion material.

- [ ] **Step 4: Verify the serialized shader graph contains the new property and alpha-capable state**

Run:

```bash
rg '"_TransparencyMap"|m_AlphaMode|m_AlphaClip' UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph
```

Expected: `_TransparencyMap` appears in the serialized property list and alpha mode settings remain explicit in the graph file

- [ ] **Step 5: Commit**

```bash
git add UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat
git commit -m "feat: add transparency map shader support"
```

### Task 6: Run Final Validation In Unity And Regression Checks

**Files:**
- Modify: `UmaConverterUnity/Editor/UMAConverter.cs`
- Modify: `UmaConverterUnity/Editor/UMAConverterSettings.cs`
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset`
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat`
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset`
- Modify: `UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat`
- Modify: `UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph`
- Test: `UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs`

- [ ] **Step 1: Re-run the EditMode tests**

Run in Unity Editor: `Window > General > Test Runner > EditMode > Run All`
Expected: all `TransparencyOverlayPolicyTests` pass

- [ ] **Step 2: Run repository-level regression searches for existing channels**

Run:

```bash
rg "SpecGlossMap|Diffuse|BaseMap|Normal|OcclusionMap|TransparencyMap|transparentMaterial|CCTransparentMaterial" UmaConverterUnity/Editor/UMAConverter.cs UmaConverterUnity/Editor/UMAConverterSettings.cs UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat
```

Expected: legacy channels still appear, `TransparencyMap` is present in the new importer/material contract, and the transparent companion material is referenced by settings

- [ ] **Step 3: Import a known transparent overlay sample in Unity and inspect the material**

Manual validation in Unity Editor:

```text
1. Import a sample asset whose overlay name contains Eyelashes or Cornea.
2. Place files such as Eyelashes_TransparencyMap.png and Eyelashes_Diffuse.png in the converter search folder.
3. Run the converter.
4. Inspect the generated overlay and material in the Editor.
5. Confirm the transparency texture is assigned and the transparent surface path renders as expected.
6. Repeat with Body_TransparencyMap.png and confirm the body overlay stays opaque.
```

Expected: eyelashes/cornea-like overlays render transparently, body-like overlays stay opaque

- [ ] **Step 4: Review the final diff for scope**

Run:

```bash
git diff -- UmaConverterUnity/Editor/UMAConverter.cs UmaConverterUnity/Editor/UMAConverterSettings.cs UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs UmaConverterUnity/Editor/UMAConverter.Editor.asmdef UmaConverterUnity/Tests/Editor/UMAConverter.EditorTests.asmdef UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph
```

Expected: only transparency-policy, UMAMaterial, and shader-related changes are present

- [ ] **Step 5: Commit**

```bash
git add UmaConverterUnity/Editor/UMAConverter.cs UmaConverterUnity/Editor/UMAConverterSettings.cs UmaConverterUnity/Editor/Transparency/TransparencyOverlayPolicy.cs UmaConverterUnity/Editor/UMAConverter.Editor.asmdef UmaConverterUnity/Tests/Editor/UMAConverter.EditorTests.asmdef UmaConverterUnity/Tests/Editor/TransparencyOverlayPolicyTests.cs UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.asset UmaConverterUnity/Runtime/UMAMaterials/CCTransparentMaterial.mat UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph
git commit -m "feat: support transparency maps for known overlays"
```