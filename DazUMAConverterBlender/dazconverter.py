import bpy
import importlib
import os
import glob

if "dataHandling" in locals():
    importlib.reload(dataHandling)
else:
    from . import dataHandling

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
    Detects if any armature in the scene is a Daz Genesis rig, UMA rig, and determines generation.
    Returns:
        dict: {
            'is_daz_rig': bool,
            'is_uma_rig': bool,
            'generation': str
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
    try:
        texture_node.image = bpy.data.images.load(texture_path)
    except RuntimeError:
        nodes.remove(texture_node)
        return
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
        found_files = _find_textures(search_base_path, material.name)
        loaded_types = set()
        for file in found_files:
            lower = file.lower()
            if "_roughness" in lower and "roughness" not in loaded_types:
                _add_texture_to_material(material, file, "roughness")
                loaded_types.add("roughness")
            elif "_metallic" in lower and "metallic" not in loaded_types:
                _add_texture_to_material(material, file, "metallic")
                loaded_types.add("metallic")
