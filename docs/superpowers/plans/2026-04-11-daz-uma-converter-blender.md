# DazUMAConverterBlender Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a standalone Blender addon `DazUMAConverterBlender` that converts Daz3D Genesis (Gen3/8/8.1/9) characters to UMA-compatible FBX + JSON files consumable by the existing `UmaConverterUnity` plugin without any Unity-side changes.

**Architecture:** Thin Daz adapter that mirrors `UMAConverterBlender` exactly — `dataHandling.py` and `gui.py` are copied verbatim (they contain no source-specific logic); only `dazconverter.py` and `__init__.py` are written fresh with Daz bone names and rig detection logic.

**Tech Stack:** Python 3, Blender Python API (`bpy`), FBX export/import operators, JSON

**Spec:** `docs/superpowers/specs/2026-04-11-daz-uma-converter-blender-design.md`

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `DazUMAConverterBlender/__init__.py` | Create | `bl_info`, Panel, Operators (`Import`, `Convert`, `Export`), `register`/`unregister` |
| `DazUMAConverterBlender/dazconverter.py` | Create | Daz rig detection, hip height, UMA bone setup, apply transforms, material setup |
| `DazUMAConverterBlender/dataHandling.py` | Copy | Data classes (`UMAData_Race`, `UMAData_Cloth`, `UMAData_Slot`) + JSON/scene serialisation |
| `DazUMAConverterBlender/gui.py` | Copy | Blender scene properties, `MeshItem` property group, wardrobe slot search |

---

## Task 1: Scaffold folder + copy shared files

**Files:**
- Create: `DazUMAConverterBlender/dataHandling.py`
- Create: `DazUMAConverterBlender/gui.py`

These files are copied verbatim because they contain no CC4-specific logic. Copying them (rather than importing cross-addon) keeps the addon self-contained and installable as a standalone zip.

- [ ] **Step 1: Create the addon directory and copy the two shared files**

```bash
cp UMAConverterBlender/dataHandling.py DazUMAConverterBlender/dataHandling.py
cp UMAConverterBlender/gui.py          DazUMAConverterBlender/gui.py
```

- [ ] **Step 2: Verify copies are identical to source**

```bash
diff UMAConverterBlender/dataHandling.py DazUMAConverterBlender/dataHandling.py
diff UMAConverterBlender/gui.py          DazUMAConverterBlender/gui.py
```

Expected output: no diff (empty / silent).

- [ ] **Step 3: Commit**

```bash
git add DazUMAConverterBlender/dataHandling.py DazUMAConverterBlender/gui.py
git commit -m "feat(daz): scaffold addon folder, copy shared dataHandling and gui"
```

---

## Task 2: Create `dazconverter.py` — rig detection

**Files:**
- Create: `DazUMAConverterBlender/dazconverter.py`

`check_rig()` is the first function the panel calls. It must work correctly before any other button is clickable. We write and manually test this in isolation.

- [ ] **Step 1: Create `DazUMAConverterBlender/dazconverter.py` with `check_rig()`**

```python
import bpy
import importlib
import os
import glob

if "dataHandling" in locals():
    importlib.reload(dataHandling)
else:
    from . import dataHandling


# ── Generation fingerprint ──────────────────────────────────────────────────
# Each generation has a unique bone that is a child of 'hip'.
_GENERATION_SIGNATURES = {
    "G3": "abdomen",
    "G8": "abdomenLower",
    "G9": "spine1",
}


def _detect_generation(armature_obj):
    """Return 'G3', 'G8', 'G9', or 'unknown' by inspecting children of 'hip'."""
    bones = armature_obj.data.bones
    if "hip" not in bones:
        return "unknown"
    hip_children = {b.name for b in bones["hip"].children}
    for gen, signature in _GENERATION_SIGNATURES.items():
        if signature in hip_children:
            return gen
    return "unknown"


def check_rig():
    """
    Returns a dict:
      {
        "is_daz_rig": bool,   # Daz Genesis rig, not yet converted
        "is_uma_rig": bool,   # Already converted (Global > Position present)
        "generation": str     # "G3", "G8", "G9", or "unknown"
      }
    """
    result = {"is_daz_rig": False, "is_uma_rig": False, "generation": "unknown"}
    for obj in bpy.data.objects:
        if obj.type != "ARMATURE":
            continue
        bones = obj.data.bones

        # UMA rig: Global bone has Position as a child
        if "Global" in bones:
            pos_children = {b.name for b in bones["Global"].children}
            if "Position" in pos_children:
                result["is_uma_rig"] = True

        # Daz rig: 'hip' root bone present, no CC4 bones present
        if "hip" in bones and "CC_Base_Hip" not in bones:
            result["is_daz_rig"] = True
            result["generation"] = _detect_generation(obj)

    return result
```

- [ ] **Step 2: Manual smoke-test in Blender Script Console**

Open Blender, paste and run:

```python
import sys
sys.path.insert(0, "/path/to/CC2UMAConverter/DazUMAConverterBlender")
import dazconverter
print(dazconverter.check_rig())
# Expected on empty scene: {'is_daz_rig': False, 'is_uma_rig': False, 'generation': 'unknown'}
```

- [ ] **Step 3: Commit**

```bash
git add DazUMAConverterBlender/dazconverter.py
git commit -m "feat(daz): add check_rig() with generation detection"
```

---

## Task 3: Add hip height functions to `dazconverter.py`

**Files:**
- Modify: `DazUMAConverterBlender/dazconverter.py`

`get_daz_hip_height_global()` is required for Race conversion (stores the hip height in the JSON). `adjust_daz_hip_height()` is required for Clothing conversion (normalises height to the saved race).

- [ ] **Step 1: Append hip height functions to `dazconverter.py`**

```python
def get_daz_hip_height_global():
    """
    Returns the world-Z position of the 'hip' bone head.
    Returns None if no armature or 'hip' bone is found.
    """
    bpy.ops.object.mode_set(mode="OBJECT")
    armature = next(
        (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None
    )
    if armature is None:
        print("No armature found.")
        return None
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    pose_bone = armature.pose.bones.get("hip")
    if pose_bone is None:
        print("'hip' bone not found.")
        bpy.ops.object.mode_set(mode="OBJECT")
        return None
    world_position = armature.matrix_world @ pose_bone.head
    bpy.ops.object.mode_set(mode="OBJECT")
    return world_position.z


def adjust_daz_hip_height(y_delta):
    """
    Adjusts the Y position of the 'hip' bone in Pose mode by y_delta.
    Used during clothing conversion to normalise height to the saved race template.
    """
    bpy.ops.object.mode_set(mode="OBJECT")
    armature = next(
        (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None
    )
    if armature is None:
        print("No armature found.")
        return
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bone = armature.data.edit_bones.get("hip")
    if bone:
        bone.select = True
        bone.select_head = True
        bone.select_tail = True
        bpy.ops.armature.parent_clear(type="DISCONNECT")
    bpy.ops.object.mode_set(mode="POSE")
    pose_bone = armature.pose.bones.get("hip")
    if pose_bone:
        pose_bone.location[1] = y_delta
    bpy.ops.object.mode_set(mode="OBJECT")
```

- [ ] **Step 2: Commit**

```bash
git add DazUMAConverterBlender/dazconverter.py
git commit -m "feat(daz): add get_daz_hip_height_global and adjust_daz_hip_height"
```

---

## Task 4: Add `apply_transforms_rest_pose()` and `add_uma_bones()` to `dazconverter.py`

**Files:**
- Modify: `DazUMAConverterBlender/dazconverter.py`

These two functions are the core of the conversion step. `apply_transforms_rest_pose()` is copied verbatim from the CC4 plugin (it is not CC4-specific). `add_uma_bones()` is rewritten for Daz: instead of renaming `root` → `Position`, it wraps the existing `hip` bone with new `Position` and `Global` bones.

- [ ] **Step 1: Append both functions to `dazconverter.py`**

```python
def apply_transforms_rest_pose():
    """
    Applies all transforms and sets the armature to rest pose.
    Daz FBX imports can have incorrect transforms; this normalises them.
    """
    armature_obj = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    if not armature_obj:
        return
    bpy.context.view_layer.objects.active = armature_obj[0]
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for obj in bpy.data.objects:
        if obj.type in {"ARMATURE", "MESH"}:
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()
    bpy.context.area.tag_redraw()


def add_uma_bones():
    """
    Inserts 'Global' and 'Position' bones above the existing 'hip' bone,
    creating the UMA-required hierarchy: Global → Position → hip → (Daz skeleton).
    Also resets mesh rotation to zero.
    """
    for obj in bpy.data.objects:
        if obj.type != "ARMATURE":
            continue
        # Only act if we have a 'hip' bone that is NOT yet under Position
        bones = obj.data.bones
        if "hip" not in bones:
            continue
        if "Position" in bones:
            continue  # already converted

        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        edit_bones = obj.data.edit_bones

        hip_bone = edit_bones.get("hip")
        if hip_bone is None:
            bpy.ops.object.mode_set(mode="OBJECT")
            continue

        # Create Position bone at the same location as hip
        position_bone = edit_bones.new("Position")
        position_bone.head = hip_bone.head.copy()
        position_bone.tail = hip_bone.head.copy()
        position_bone.tail.z += 1.0  # give it a valid length

        # Create Global bone at world origin
        global_bone = edit_bones.new("Global")
        global_bone.head = (0.0, 0.0, 0.0)
        global_bone.tail = (0.0, 0.0, 1.0)

        # Wire up the hierarchy
        position_bone.parent = global_bone
        hip_bone.parent = position_bone

        bpy.ops.object.mode_set(mode="OBJECT")

    # Reset mesh rotations
    for mesh_obj in bpy.data.objects:
        if mesh_obj.type == "MESH":
            mesh_obj.rotation_euler = (0.0, 0.0, 0.0)
```

- [ ] **Step 2: Commit**

```bash
git add DazUMAConverterBlender/dazconverter.py
git commit -m "feat(daz): add apply_transforms_rest_pose and add_uma_bones"
```

---

## Task 5: Add `setup_daz_materials()` and mesh/overlay helpers to `dazconverter.py`

**Files:**
- Modify: `DazUMAConverterBlender/dazconverter.py`

`setup_daz_materials()` replaces `setup_pbr_materials()` from the CC4 plugin. Daz uses `_Roughness` and `_Metallic` suffixes (capitalised), so the suffix matching is updated. `meshes_to_overlay()` and `mesh_to_overlay()` are copied verbatim from the CC4 plugin.

- [ ] **Step 1: Append material and overlay helpers to `dazconverter.py`**

```python
def meshes_to_overlay(mesh_names):
    """Returns a deduplicated list of material names used by the given meshes."""
    unique_materials = set()
    for mesh_name in mesh_names:
        mesh = bpy.data.objects.get(mesh_name)
        if mesh and mesh.type == "MESH":
            for mat_slot in mesh.material_slots:
                if mat_slot.material:
                    unique_materials.add(mat_slot.material.name)
    return list(unique_materials)


def mesh_to_overlay(mesh_name):
    """Returns the first material name of a given mesh (the UMA overlay name)."""
    mesh = bpy.data.objects.get(mesh_name)
    if mesh and mesh.type == "MESH":
        for mat_slot in mesh.material_slots:
            if mat_slot.material:
                return mat_slot.material.name
    return ""


def _find_textures(base_path, search_pattern):
    pattern = os.path.join(base_path, "**", search_pattern + "*.*")
    return glob.glob(pattern, recursive=True)


def _add_texture_to_material(material, texture_path, texture_type):
    """Adds an image texture node to a PBR material and wires it to the BSDF."""
    if not material.use_nodes:
        material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links

    texture_node = nodes.new(type="ShaderNodeTexImage")
    texture_node.image = bpy.data.images.load(texture_path)
    texture_node.location = (0, 0)
    texture_node.name = texture_node.label = texture_type

    if texture_type in ("roughness", "metallic"):
        texture_node.image.colorspace_settings.name = "Non-Color"

    bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        if texture_type == "metallic":
            links.new(texture_node.outputs["Color"], bsdf.inputs["Metallic"])
        elif texture_type == "roughness":
            links.new(texture_node.outputs["Color"], bsdf.inputs["Roughness"])


def setup_daz_materials(search_base_path):
    """
    Scans the FBX directory for Daz texture files and assigns them to materials.
    Supports both default Daz export naming (_Roughness, _Metallic)
    and iRay baked naming (_roughness, _metallic — lowercase).
    Unmatched textures are skipped silently.
    """
    for material in bpy.data.materials:
        if not material.use_nodes:
            continue
        # Daz material names are surface names; try matching directly
        found_files = _find_textures(search_base_path, material.name)
        for file in found_files:
            lower = file.lower()
            if "_roughness" in lower:
                _add_texture_to_material(material, file, "roughness")
            elif "_metallic" in lower:
                _add_texture_to_material(material, file, "metallic")
```

- [ ] **Step 2: Commit**

```bash
git add DazUMAConverterBlender/dazconverter.py
git commit -m "feat(daz): add setup_daz_materials, meshes_to_overlay, mesh_to_overlay"
```

---

## Task 6: Create `__init__.py` — Panel and Operators

**Files:**
- Create: `DazUMAConverterBlender/__init__.py`

This is the Blender addon entry point. It wires together all modules and defines the UI panel and three operators: Import, Convert, Export. The logic is structurally identical to `UMAConverterBlender/__init__.py`; differences are Daz-specific labels, operator IDs prefixed `dazuma`, and calls to `dazconverter` instead of `umaconverter`.

- [ ] **Step 1: Create `DazUMAConverterBlender/__init__.py`**

```python
bl_info = {
    "name": "Daz UMA Converter",
    "blender": (2, 93, 0),
    "category": "Object",
    "version": (0, 1, 0),
    "author": "Valentin Winkelmann",
    "description": (
        "Converts Daz3D Genesis characters (Gen3/8/8.1/9) to UMA-compatible assets "
        "for use with the UMA Framework in Unity."
    ),
}

import bpy
import importlib
import os
import shutil
from bpy_extras.io_utils import ImportHelper, ExportHelper
from bpy.types import Operator, Panel
from bpy.props import StringProperty, EnumProperty, BoolProperty

if "dataHandling" in locals():
    importlib.reload(dataHandling)
else:
    from . import dataHandling

if "dazconverter" in locals():
    importlib.reload(dazconverter)
else:
    from . import dazconverter

if "gui" in locals():
    importlib.reload(gui)
else:
    from . import gui


# ── Viewport helper ──────────────────────────────────────────────────────────

def init_blender_viewport():
    for area in bpy.context.screen.areas:
        if area.type == "VIEW_3D":
            space = area.spaces.active
            if hasattr(space, "shading"):
                space.shading.type = "MATERIAL"
                break


# ── Panel ────────────────────────────────────────────────────────────────────

class DAZUMA_PT_Panel(Panel):
    bl_label = "Daz UMA Converter"
    bl_idname = "DAZUMA_PT_Panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Daz UMA Converter"

    def draw(self, context):
        layout = self.layout
        rig_status = dazconverter.check_rig()

        layout.label(text="Daz UMA Converter v0.1.0")

        if rig_status["is_daz_rig"]:
            gen = rig_status["generation"]
            layout.label(text=f"Daz Rig found ({gen})", icon="INFO")
            layout.prop(context.scene, "rig_type", text="Rig Type")

            if context.scene.rig_type == "clothing":
                box = layout.box()
                box.label(text="Clothing Options")
                box.prop(context.scene, "json_file_path", text="JSON File Path")

            layout.operator("dazuma.convert", text="Convert")

        elif rig_status["is_uma_rig"]:
            if context.scene.rig_type == "race":
                layout.prop(context.scene, "race_name")

            race_data = None
            if context.scene.rig_type == "clothing":
                race_data = dataHandling.load_from_scene_properties(
                    "race_data", dataHandling.UMAData_Race
                )
                box = layout.box()
                box.label(text="UMA Race Info", icon="INFO")
                box.label(text="Clothing for: " + race_data.name)

            layout.separator()
            layout.label(text="Available Overlays:")
            overlays = dazconverter.meshes_to_overlay(
                [item.name for item in context.scene.mesh_items if item.selected]
            )
            for overlay in overlays:
                row = layout.row()
                row.label(text=overlay, icon="MATERIAL")

            layout.label(text="Available Meshes:")
            for item in context.scene.mesh_items:
                if race_data is not None and item.name in race_data.meshes:
                    continue
                box = layout.box()
                row = box.row()
                row.prop(item, "selected", text=item.name)
                if item.selected:
                    row.prop(item, "slot_name", text="Slot Name")
                if context.scene.rig_type == "clothing" and item.selected:
                    box.prop(item, "wardrobe_slot")

            layout.operator("dazuma.export", text="Export Selected")

        else:
            layout.label(text="Please import a Daz Genesis FBX", icon="INFO")
            layout.operator("dazuma.import", text="Import FBX")


# ── Operators ────────────────────────────────────────────────────────────────

class DAZUMA_OT_Convert(Operator):
    bl_idname = "dazuma.convert"
    bl_label = "Convert"

    def execute(self, context):
        init_blender_viewport()
        dazconverter.apply_transforms_rest_pose()

        if context.scene.rig_type == "race":
            hip_height = dazconverter.get_daz_hip_height_global()
            race_data = dataHandling.UMAData_Race("NewRace", hip_height, [], [], [])
            dataHandling.save_to_scene_properties(race_data, "race_data")

        if context.scene.rig_type == "clothing":
            race_data = dataHandling.load_from_json_file(
                context.scene.json_file_path, dataHandling.UMAData_Race
            )
            dataHandling.save_to_scene_properties(race_data, "race_data")
            current_hip = dazconverter.get_daz_hip_height_global()
            difference = current_hip - race_data.hipHeight
            dazconverter.adjust_daz_hip_height(-difference)

        dazconverter.add_uma_bones()
        self.report({"INFO"}, "Conversion complete.")
        return {"FINISHED"}


class DAZUMA_OT_Import(Operator, ImportHelper):
    bl_idname = "dazuma.import"
    bl_label = "Import FBX"
    filename_ext = ".fbx"
    filter_glob: StringProperty(default="*.fbx", options={"HIDDEN"})

    def execute(self, context):
        import_options = {
            "use_anim": False,
            "ignore_leaf_bones": True,
            "automatic_bone_orientation": False,
        }
        bpy.ops.import_scene.fbx(filepath=self.filepath, **import_options)
        bpy.context.scene.mesh_items.clear()
        for obj in bpy.data.objects:
            if obj.type == "MESH":
                item = bpy.context.scene.mesh_items.add()
                item.name = obj.name
        file_dir = os.path.dirname(self.filepath)
        dazconverter.setup_daz_materials(file_dir)
        self.report({"INFO"}, "FBX imported successfully.")
        return {"FINISHED"}


class DAZUMA_OT_Export(Operator, ExportHelper):
    bl_idname = "dazuma.export"
    bl_label = "Export Selected"
    filename_ext = ".fbx"

    export_textures: BoolProperty(
        name="Export Textures",
        description="Copy textures alongside the exported FBX",
        default=True,
    )

    def execute(self, context):
        bpy.ops.object.select_all(action="DESELECT")
        for item in context.scene.mesh_items:
            if item.selected:
                mesh_obj = bpy.data.objects.get(item.name)
                if mesh_obj:
                    mesh_obj.select_set(True)
        for obj in bpy.data.objects:
            if obj.type == "ARMATURE":
                obj.select_set(True)
                context.view_layer.objects.active = obj

        bpy.ops.export_scene.fbx(
            filepath=self.filepath,
            use_selection=True,
            global_scale=0.01,
            object_types={"MESH", "ARMATURE"},
            add_leaf_bones=False,
        )

        filename_no_ext = self.filepath.replace(".fbx", "")

        if context.scene.rig_type == "race":
            race_data = dataHandling.load_from_scene_properties(
                "race_data", dataHandling.UMAData_Race
            )
            race_data.name = context.scene.race_name
            race_data.meshes = [
                item.name for item in context.scene.mesh_items if item.selected
            ]
            race_data.overlays = dazconverter.meshes_to_overlay(race_data.meshes)
            race_data.slots = []
            for item in context.scene.mesh_items:
                if item.selected:
                    slot = dataHandling.UMAData_Slot(
                        item.slot_name,
                        item.name,
                        dazconverter.mesh_to_overlay(item.name),
                    )
                    race_data.slots.append(slot)
            dataHandling.save_to_json_file(race_data, filename_no_ext + "_race.json")

        if context.scene.rig_type == "clothing":
            race_data = dataHandling.load_from_scene_properties(
                "race_data", dataHandling.UMAData_Race
            )
            cloth_data = dataHandling.UMAData_Cloth([race_data.name], [], [], [])
            cloth_data.meshes = [
                item.name for item in context.scene.mesh_items if item.selected
            ]
            cloth_data.overlays = dazconverter.meshes_to_overlay(cloth_data.meshes)
            cloth_data.slots = []
            for item in context.scene.mesh_items:
                if item.selected:
                    slot = dataHandling.UMAData_Slot(
                        item.slot_name,
                        item.name,
                        dazconverter.mesh_to_overlay(item.name),
                    )
                    slot.wardrobeSlot = item.wardrobe_slot
                    cloth_data.slots.append(slot)
            dataHandling.save_to_json_file(cloth_data, filename_no_ext + "_cloth.json")

        if self.export_textures:
            selected_objects = [
                bpy.data.objects[item.name]
                for item in context.scene.mesh_items
                if item.selected
            ]
            _save_textures(self.filepath, selected_objects, filename_no_ext)

        self.report({"INFO"}, "Export successful.")
        return {"FINISHED"}

    def draw(self, context):
        self.layout.prop(self, "export_textures")


# ── Texture export helper ────────────────────────────────────────────────────

def _save_textures(filepath, selected_objects, custom_folder_name):
    base_dir = os.path.dirname(filepath)
    texture_dir = os.path.join(base_dir, custom_folder_name, "Textures")
    os.makedirs(texture_dir, exist_ok=True)
    for obj in selected_objects:
        if obj.type != "MESH" or not obj.material_slots:
            continue
        for slot in obj.material_slots:
            if not slot.material or not slot.material.use_nodes:
                continue
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    src = bpy.path.abspath(node.image.filepath)
                    if os.path.isfile(src):
                        suffix = os.path.basename(src).split("_")[-1]
                        dest_name = f"{slot.material.name}_{suffix}"
                        shutil.copy(src, os.path.join(texture_dir, dest_name))


# ── Register / Unregister ────────────────────────────────────────────────────

def register():
    gui.register_rig_type_selector()
    gui.register_json_file_field()
    gui.register_race_wizard()
    gui.register_mesh_items()
    bpy.utils.register_class(DAZUMA_PT_Panel)
    bpy.utils.register_class(DAZUMA_OT_Convert)
    bpy.utils.register_class(DAZUMA_OT_Import)
    bpy.utils.register_class(DAZUMA_OT_Export)


def unregister():
    gui.unregister_rig_type_selector()
    gui.unregister_json_file_field()
    gui.unregister_race_wizard()
    gui.unregister_mesh_items()
    bpy.utils.unregister_class(DAZUMA_PT_Panel)
    bpy.utils.unregister_class(DAZUMA_OT_Convert)
    bpy.utils.unregister_class(DAZUMA_OT_Import)
    bpy.utils.unregister_class(DAZUMA_OT_Export)


if __name__ == "__main__":
    register()
```

- [ ] **Step 2: Verify the file parses without syntax errors (no Blender needed)**

```bash
python3 -c "
import ast, sys
with open('DazUMAConverterBlender/__init__.py') as f:
    src = f.read()
try:
    ast.parse(src)
    print('OK: no syntax errors')
except SyntaxError as e:
    print('SYNTAX ERROR:', e)
    sys.exit(1)
"
```

Expected output: `OK: no syntax errors`

- [ ] **Step 3: Commit**

```bash
git add DazUMAConverterBlender/__init__.py
git commit -m "feat(daz): add __init__.py with panel, import/convert/export operators"
```

---

## Task 7: Update `readme.md`

**Files:**
- Modify: `readme.md`

Add a `DazUMAConverterBlender` section after the existing CC4 content, covering: what it is, Daz export steps, and how to install the addon.

- [ ] **Step 1: Add the following section to the end of `readme.md` (before the License section)**

```markdown
---

# 🧬 Daz3D To UMA [ Experimental ]
## 📖Description
`DazUMAConverterBlender` is a companion Blender plugin that converts Daz3D Genesis characters (Genesis 3, 8, 8.1, 9) to UMA-compatible assets using the same pipeline as the Character Creator 4 plugin. The output is fully compatible with the existing **UmaConverterUnity** plugin — no Unity-side changes required.

## 🛠️Installation
### Blender Plugin
1. Download or clone this repository.
2. Open Blender and go to **Edit > Preferences > Add-ons > Install**.
3. Select `DazUMAConverterBlender` as a folder (zip the folder if needed).
4. Enable the Add-on by checking the box next to **Daz UMA Converter**.
5. Click **File > Defaults > Save Startup File**.

## 🎛️Usage

### 📦 Daz3D Export
Export your character from Daz Studio as FBX:
1. **File > Export As > FBX**
2. Set **Morphs** to disabled (morphs are out of scope).
3. Enable **Merge Clothing into Figure Skeleton** for clothing exports.
4. Save textures alongside the FBX in the same folder.

### 🩻 Create a new UMA Race
1. In Blender open the **Daz UMA Converter** panel (N-panel, `Daz UMA Converter` tab).
2. Click **Import FBX** and select your exported Daz character FBX.
3. The plugin detects the rig generation (G3/G8/G9).
4. Set **Rig Type** to `Race` and press **Convert**.
5. Select meshes, fill in Slot Names, then press **Export Selected**.
6. Drag both the `.fbx` and `_race.json` into your Unity project.

### 👕 Create UMA Clothing
1. Import a clothed Daz character FBX.
2. Set **Rig Type** to `Clothing`, select the `_race.json` from your previously exported Race.
3. Press **Convert**, select clothing meshes, set Slot Names and Wardrobe Slot Types.
4. Press **Export Selected**.
5. Drag the `.fbx` and `_cloth.json` into Unity.

### ⚠️ Important Notes
- Textures must be exported alongside the FBX in the same directory.
- Only PBR (Principled BSDF) materials are supported. Daz's Iray skin/eye/hair shaders are not handled.
- Morphs, shape keys, strand hair, and non-skinned accessories are out of scope.
- If you export clothing for a race, use exactly the same base character for all clothing exports.
```

- [ ] **Step 2: Commit**

```bash
git add readme.md
git commit -m "docs: add DazUMAConverterBlender section to readme"
```

---

## Task 8: End-to-end manual verification checklist

No automated tests are possible without a running Blender instance. Use this checklist after installing the addon in Blender.

- [ ] **Install addon:** Zip `DazUMAConverterBlender/`, install in Blender → `Daz UMA Converter` tab appears in N-panel.
- [ ] **Empty scene:** Panel shows `"Please import a Daz Genesis FBX"` and Import button.
- [ ] **Import a Genesis 8 FBX:** Panel shows `"Daz Rig found (G8)"`, Rig Type selector, and Convert button.
- [ ] **Race Convert:** Press Convert with Rig Type = Race → panel switches to UMA mode, mesh list appears.
- [ ] **Race Export:** Select mesh, fill Slot Name, press Export → `.fbx` and `_race.json` written to disk. Open JSON and verify `type == "race"`, `hipHeight` is non-zero, `slots` has the correct name/mesh/overlay.
- [ ] **Clothing Convert:** New scene, import clothed Genesis 8 FBX, Rig Type = Clothing, select `_race.json` → Convert, Export → `_cloth.json` written. Verify `type == "cloth"`, `compatibleRaces` lists the race name.
- [ ] **Unity round-trip:** Drop FBX + JSON into a Unity project with UmaConverterUnity installed → UMA assets auto-generated without errors.
- [ ] **Genesis 3 smoke test:** Repeat Race Convert with a G3 FBX → label shows `"Daz Rig found (G3)"`.
- [ ] **Genesis 9 smoke test:** Repeat Race Convert with a G9 FBX → label shows `"Daz Rig found (G9)"`.

---

## Self-Review Notes

| Spec requirement | Task that covers it |
|---|---|
| Standalone folder `DazUMAConverterBlender/` | Task 1 |
| `dataHandling.py` and `gui.py` copied verbatim | Task 1 |
| `check_rig()` with G3/G8/G9 detection | Task 2 |
| Hip height read + adjust | Task 3 |
| `apply_transforms_rest_pose()` and `add_uma_bones()` | Task 4 |
| `setup_daz_materials()` + overlay helpers | Task 5 |
| Panel, Import/Convert/Export operators | Task 6 |
| readme update | Task 7 |
| No Unity-side changes | ✅ — confirmed, no UmaConverterUnity files touched |
| JSON output identical to CC4 contract | ✅ — same `UMAData_Race`/`UMAData_Cloth` classes, same export filenames |
