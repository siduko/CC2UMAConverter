# TransparencyMap Support Design

## Goal

Add TransparencyMap support to the Unity UMA conversion pipeline so transparent source materials such as eyelashes and eye surfaces can render correctly, while preserving the current opaque default for all other overlays.

## Current State

- Texture discovery is driven by `textureChannelAliases` in `UmaConverterUnity/Editor/UMAConverter.cs`.
- Overlay textures are resolved in `GetOverlayTextureList()` and assigned in `ApplyOverlayData()`.
- The default UMA material contract is defined by `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset` and `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat`.
- The active SRP shader graph in `UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph` is currently opaque and does not expose an alpha output path.

## Recommended Approach

Use scoped transparency support.

This approach adds TransparencyMap handling across the importer, UMAMaterial contract, and shader/material pipeline, but only enables alpha blending for overlays that are explicitly known to be transparent or match a narrow fallback rule. Unknown overlays remain opaque even if a transparency-like texture exists.

This is preferred over import-only support because import-only does not solve the rendering problem, and over global alpha blending because it is too risky for mixed Daz/CC export data where opacity-like textures may be present on materials that should still render as opaque.

## Functional Requirements

### Texture Alias Support

The converter must recognize `TransparencyMap` and common equivalents when looking up overlay textures.

Initial alias coverage:

- `TransparencyMap`
- `Opacity`
- `Alpha`
- `OpacityMask`

Additional aliases can be added later if new exports require them, but the first implementation should stay narrow.

### Transparency Decision Policy

Transparency is decided per overlay, not globally.

Rules:

1. If no transparency-like texture is resolved, the overlay stays opaque.
2. If a transparency-like texture is resolved and the overlay is on the known transparent allowlist, enable alpha blending.
3. If a transparency-like texture is resolved and the overlay is not on the allowlist, keep the overlay opaque by default.
4. If the overlay name matches a narrow fallback pattern that strongly implies transparency, enable alpha blending.

Initial allowlist and fallback keywords:

- `lash`
- `eyelash`
- `cornea`
- `moisture`
- `tear`
- `glass`
- `lens`

The first version should not analyze texture pixels, infer thresholds from alpha coverage, or automatically switch to alpha clip.

### Rendering Mode

Transparent overlays use alpha blending.

Opaque overlays continue using the current opaque path.

The first version intentionally excludes:

- alpha clip / cutout mode
- automatic switching between blend and clip
- per-material user configuration UI
- texture content heuristics

## Architecture

### 1. Import Layer

Extend `textureChannelAliases` in `UmaConverterUnity/Editor/UMAConverter.cs` with transparency aliases.

The existing texture lookup path in `GetOverlayTextureList()` remains the main resolution mechanism. The change here is only to make the transparency channel discoverable through the same candidate-based lookup already used for diffuse, normal, roughness, and specular-like channels.

### 2. Overlay Assignment Layer

Add a small helper near the overlay-building path in `UmaConverterUnity/Editor/UMAConverter.cs` that answers:

- whether the resolved texture set contains a transparency texture
- whether the overlay should use the transparent rendering path

`ApplyOverlayData()` is the preferred integration point because it already resolves textures for the overlay and chooses between slot material textures and default material textures.

This helper should be policy-only. It should not change how textures are searched for, only how the resolved result is interpreted.

### 3. Material Contract Layer

Update the default UMA material assets so the transparency texture has an explicit channel and serialized material property.

Files:

- `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.asset`
- `UmaConverterUnity/Runtime/UMAMaterials/CCMaterial.mat`

This keeps the transparency texture in the same data contract as the existing diffuse, normal, metallic, roughness, occlusion, emission, and spec/gloss textures.

### 4. Shader Layer

Update `UmaConverterUnity/Runtime/Shaders/CC4PBR.shadergraph` so the shader can consume the transparency texture and expose an alpha blend path while preserving the existing opaque path.

The shader must remain safe by default:

- opaque rendering is still the baseline behavior
- transparent output is activated only when the converter has identified an overlay as transparency-capable

## Data Flow

1. UMA material channels define that a transparency texture is supported.
2. `GetOverlayTextureList()` resolves a transparency-like texture from file names such as `Overlay_TransparencyMap`, `Overlay_Opacity`, or `Overlay_Alpha`.
3. `ApplyOverlayData()` assigns the resolved texture list and evaluates whether the overlay should use transparent rendering.
4. The material/shader path consumes the transparency texture and renders in alpha blend mode only for overlays approved by the scoped policy.

## Testing Strategy

The first-pass validation should focus on behavior.

Required checks:

1. Alias resolution works for `TransparencyMap`, `Opacity`, `Alpha`, and `OpacityMask`.
2. Overlays without transparency textures remain opaque.
3. Known transparent overlays with a transparency texture render through the alpha blend path.
4. Unknown overlays with a transparency texture remain opaque unless they match the narrow fallback keywords.
5. Existing channels such as `Diffuse`, `BaseMap`, `Normal`, `OcclusionMap`, and `SpecGlossMap` continue to resolve exactly as before.

## Risks And Mitigations

### Risk: False positives make body or clothing materials transparent

Mitigation: keep the default opaque and only enable alpha blending for allowlisted or narrowly matched overlays.

### Risk: Shader changes accidentally alter all materials

Mitigation: preserve the opaque path as the default and gate transparent behavior behind explicit overlay classification.

### Risk: Alias expansion breaks existing texture matching

Mitigation: add regression coverage for existing channel aliases and keep transparency aliases confined to the new channel.

## Out Of Scope

- alpha clip or cutout support
- user-editable transparency rules in the UI
- automatic texture-content analysis
- per-project material rule assets
- broad shader refactors unrelated to transparency

## Acceptance Criteria

- Transparency-like textures can be discovered from common source naming variants.
- The default UMA material/shader pipeline accepts a transparency texture.
- Known transparent overlays render with alpha blending.
- Unknown overlays remain opaque by default.
- Existing non-transparency channel resolution remains unchanged.