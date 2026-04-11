# DazUMAConverterBlender — Design Spec

## Problem

The existing `UMAConverterBlender` addon converts Character Creator 4 characters to UMA assets via a Blender-to-Unity pipeline. Daz3D users (Genesis 3, 8, 8.1, 9) cannot use this pipeline because CC4 and Daz3D rigs have entirely different bone names and structures.

## Goal

Create `DazUMAConverterBlender`, a standalone Blender addon that converts Daz3D Genesis characters (Gen3, Gen8/8.1, Gen9) to UMA-compatible assets. The output FBX and JSON files must be 100% compatible with the existing `UmaConverterUnity` plugin — no Unity-side changes required.

---

## Approach

**Thin Daz Adapter.** Mirror the CC4 plugin structure exactly. Only replace CC4-specific rig detection and bone logic with Daz equivalents. All other systems (data models, JSON contract, export flow, UI structure, texture handling) are identical to the CC4 plugin.

---

## Folder Structure

```
DazUMAConverterBlender/
  __init__.py        ← bl_info, register/unregister, Panel, Operators
  dazconverter.py    ← Daz rig detection, bone ops, material setup
  dataHandling.py    ← Identical to CC4 version (no CC4-specific logic)
  gui.py             ← Identical to CC4 version (no CC4-specific logic)
```

`dataHandling.py` and `gui.py` are copied verbatim from `UMAConverterBlender/` since they contain no source-specific logic.

---

## Rig Detection

### `check_rig()` returns:
```python
{
  "is_daz_rig": bool,    # True if Daz Genesis rig detected (not yet converted)
  "is_uma_rig": bool,    # True if already converted to UMA structure
  "generation": str      # "G3", "G8", "G9", or "unknown"
}
```

### Detection logic:
- **Daz rig**: armature has a `hip` root bone AND no `CC_Base_Hip` child (to avoid false-positive on any CC4 import)
- **UMA rig**: `Global` bone exists with `Position` as child (same as CC4 plugin)
- **Generation fingerprint** (checked via child bones of `hip`):

| Generation | Signature child bone |
|---|---|
| Genesis 3  | `abdomen` |
| Genesis 8 / 8.1 | `abdomenLower` |
| Genesis 9  | `spine1` |

Unknown generation is allowed — the conversion still proceeds; only the UI label differs.

---

## Hip Height

Daz's `hip` bone serves the same role as CC4's `CC_Base_Hip`.

- `get_daz_hip_height_global()` — reads world-Z of the `hip` bone (same math as CC4's version)
- `adjust_daz_hip_height(delta)` — adjusts `hip` bone Y position in Pose mode (same as CC4's version)

Used during clothing conversion to normalize the character height to the saved race template.

---

## UMA Bone Setup (`add_uma_bones()`)

Converts the Daz rig to a UMA-compatible hierarchy:

1. The existing `hip` bone becomes the root of the character skeleton (no rename needed)
2. Create new `Position` edit bone; reparent `hip` to `Position`
3. Create new `Global` edit bone; reparent `Position` to `Global`

Final hierarchy: `Global → Position → hip → (full Daz skeleton)`

This is structurally identical to the CC4 output: `Global → Position → CC_Base_Hip → (CC4 skeleton)`.

---

## Import

FBX import options (identical to CC4 plugin):
```python
{
  'use_anim': False,
  'ignore_leaf_bones': True,
  'automatic_bone_orientation': False,
}
```

After import, all mesh objects are registered in `mesh_items`. Then `setup_daz_materials()` is called.

---

## Material Setup (`setup_daz_materials()`)

Scans the FBX directory for textures matching Daz naming conventions. Two patterns are supported (auto-detected):

- **Default Daz export**: `<CharacterName>_<SurfaceName>_<type>.png`
- **iRay baked**: same but may use `_Diffuse` / `_Specular` suffixes

The function matches textures to Blender materials by substring on surface name. It attaches `_Roughness` and `_Metallic` maps to PBR Principled BSDF nodes (non-color space). Unmatched textures are skipped gracefully without error.

---

## Export

The export pipeline is **identical** to the CC4 plugin:

1. Deselect all, then select user-chosen meshes + armature
2. Export FBX: `global_scale=0.01`, `object_types={'MESH', 'ARMATURE'}`, `add_leaf_bones=False`
3. Write JSON sidecar:
   - Race mode → `[filename]_race.json` (`UMAData_Race`)
   - Clothing mode → `[filename]_cloth.json` (`UMAData_Cloth`)
4. Optionally copy textures to `[filename]/Textures/` with `[MaterialName]_[suffix]` renaming

### JSON contract (unchanged):

**`_race.json`**
```json
{
  "type": "race",
  "name": "MyDazRace",
  "hipHeight": 0.94,
  "meshes": ["Genesis8Female"],
  "overlays": ["Skin", "Eyes"],
  "slots": [{ "name": "Body", "mesh": "Genesis8Female", "overlay": "Skin", "wardrobeSlot": "" }]
}
```

**`_cloth.json`**
```json
{
  "type": "cloth",
  "compatibleRaces": ["MyDazRace"],
  "meshes": ["Shirt"],
  "overlays": ["Fabric"],
  "slots": [{ "name": "ShirtSlot", "mesh": "Shirt", "overlay": "Fabric", "wardrobeSlot": "Chest" }]
}
```

`UmaConverterUnity` reads these files unchanged.

---

## UI Panel

| Property | Value |
|---|---|
| Panel tab | `"Daz UMA Converter"` |
| Panel id | `"DAZUMA_PT_Panel"` |
| Rig found label | `"Daz Rig found (Genesis X)"` |
| No rig label | `"Please import a Daz Genesis FBX"` |
| Import button | `"Import FBX"` |
| Convert button | `"Convert"` |
| Export button | `"Export Selected"` |

All other UI elements (Rig Type selector, Race Name field, JSON file path for clothing, mesh list with slot names and wardrobe slot picker) are identical to the CC4 plugin.

---

## Blender Addon Metadata

```python
bl_info = {
    "name": "Daz UMA Converter",
    "blender": (2, 93, 0),
    "category": "Object",
    "version": (0, 1, 0),
    "author": "Valentin Winkelmann",
    "description": "Converts Daz3D Genesis characters (Gen3/8/9) to UMA-compatible assets for use with the UMA Framework in Unity."
}
```

---

## Out of Scope

- Daz morph/shape key export
- Daz hair/strand-based hair
- Daz accessories attached via non-skinned bones
- Non-PBR material workflows (iRay skin shader, eyes, hair shaders)
- Any changes to `UmaConverterUnity`

---

## Files Changed

| File | Action |
|---|---|
| `DazUMAConverterBlender/__init__.py` | Create |
| `DazUMAConverterBlender/dazconverter.py` | Create |
| `DazUMAConverterBlender/dataHandling.py` | Copy from `UMAConverterBlender/dataHandling.py` |
| `DazUMAConverterBlender/gui.py` | Copy from `UMAConverterBlender/gui.py` |
| `readme.md` | Add DazUMAConverterBlender section |
