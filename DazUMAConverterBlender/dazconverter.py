import bpy
import bmesh
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

# Material to texture mapping for Genesis3Female
# Maps material names to glob patterns for flexible character support.
# Patterns use wildcards to match texture file names based on character/preset variations.
# Example: Eyelashes => "*_lashes_*" matches files like "RyJeane_lashes_1006.jpg" or "Character_lashes_001.jpg"
_MATERIAL_TEXTURE_MAP = {
    "Arms": "*_arms_[0-9]*",
    "Cornea": "*_eyes01_[0-9]*",
    "Ears": "*_face_[0-9]*",
    "Eyelashes": "*_lashes_[0-9]*",
    "EyeMoisture": "*_eyerefl01_[0-9]*",
    "EyeSocket": "*_eyes01_[0-9]*",
    "Face": "*_face_[0-9]*",
    "Fingernails": "*_arms_[0-9]*",
    "Irises": "*_eyes01_[0-9]*",
    "Legs": "*_legs_[0-9]*",
    "Lips": "*_mouth_[0-9]*",
    "Material": "*_torso_[0-9]*",  # Fallback/generic material
    "Mouth": "*_mouth_[0-9]*",
    "Pupils": "*_eyes01_[0-9]*",
    "Sclera": "*_eyes01_[0-9]*",
    "Teeth": "*_mouth_[0-9]*",
    "Toenails": "*_legs_[0-9]*",
    "Torso": "*_torso_[0-9]*",
}

_SPECULAR_SUFFIXES = [
    "S",
    "SP",
    "SPEC",
    "*specular*",
    "*gloss*",
    "*glossiness*",
]

_BUMP_SUFFIXES = [
    "B",
    "BM",
    "BP",
    "*bump*",
    "*height*",
    "*displacement*",
]

_NORMAL_SUFFIXES = [
    "N",
    "NM",
    "NRM",
    "NOR",
    "*normal*",
]

_ROUGHNESS_SUFFIXES = [
    "R",
    "RO",
    "*roughness*",
]

_METALLIC_SUFFIXES = [
    "M",
    "MT",
    "*metallic*",
]

# Optional additional texture maps per material.
# String values reuse the base texture pattern with the provided suffix token.
# Dict values can override the base pattern for exceptions like Lips -> lipsS.
_MATERIAL_ADDITIONAL_MAPS = {
    "Arms": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Cornea": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "Ears": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Eyelashes": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "EyeMoisture": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "EyeSocket": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "Face": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Fingernails": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Irises": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "Legs": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Lips": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": {"base_pattern": "*_lips_[0-9]*", "suffixes": _SPECULAR_SUFFIXES}, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Material": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Mouth": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Pupils": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "Sclera": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "Teeth": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES},
    "Toenails": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
    "Torso": {"roughness": _ROUGHNESS_SUFFIXES, "metallic": _METALLIC_SUFFIXES, "specular": _SPECULAR_SUFFIXES, "bump": _BUMP_SUFFIXES, "normal": _NORMAL_SUFFIXES},
}

DEBUG_DAZCONVERTER = True


def _debug_log(message):
    if DEBUG_DAZCONVERTER:
        print(f"[DazConverter][DEBUG] {message}")


def _iter_armatures_priority():
    """Yield armatures with the active armature first (if any)."""
    active = bpy.context.view_layer.objects.active
    yielded = set()
    if active is not None and active.type == "ARMATURE":
        yielded.add(active.name)
        yield active
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE" and obj.name not in yielded:
            yield obj

def _detect_generation(armature_obj):
    """Return 'G3', 'G8', 'G9', or 'unknown' from armature bone fingerprints."""
    bones = armature_obj.data.bones
    bone_names = {b.name for b in bones}

    # G9 is the most distinct signature and wins first.
    if "spine1" in bone_names:
        return "G9"

    hip_children = set()
    if "hip" in bones:
        hip_children = {b.name for b in bones["hip"].children}

    has_g3 = "abdomen" in bone_names
    has_g8 = "abdomenLower" in bone_names

    if has_g3 and not has_g8:
        return "G3"
    if has_g8 and not has_g3:
        return "G8"

    # Ambiguous rigs can contain both names. Prefer the direct hip child, then G3.
    if "abdomen" in hip_children:
        return "G3"
    if "abdomenLower" in hip_children:
        return "G8"
    if has_g3:
        return "G3"
    return "unknown"


def get_daz_hip_height_global():
    """
    Returns the world-Z position of the 'hip' bone head.
    Returns None if no armature or 'hip' bone is found.
    """
    _debug_log("get_daz_hip_height_global called")
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
    _debug_log(f"adjust_daz_hip_height called with y_delta={y_delta}")
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
    _debug_log("apply_transforms_rest_pose called")
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


def split_meshes_by_material(excluded_mesh_names=None):
    """Split meshes into one object per material while leaving excluded meshes untouched."""
    _debug_log(
        f"split_meshes_by_material called with excluded_mesh_names={excluded_mesh_names}"
    )
    excluded = set(excluded_mesh_names or [])
    split_meshes = []

    for obj in list(bpy.data.objects):
        if obj.type != "MESH" or obj.name in excluded:
            continue
        if len(obj.material_slots) <= 1:
            continue

        source_collections = list(obj.users_collection)
        source_materials = [slot.material for slot in obj.material_slots]
        created_objects = []

        for material_index, material in enumerate(source_materials):
            mesh_copy = obj.data.copy()
            clone = obj.copy()
            clone.data = mesh_copy

            if source_collections:
                for collection in source_collections:
                    collection.objects.link(clone)
            else:
                bpy.context.scene.collection.objects.link(clone)

            bm = bmesh.new()
            bm.from_mesh(mesh_copy)
            faces_to_delete = [
                face for face in bm.faces if face.material_index != material_index
            ]
            if len(faces_to_delete) == len(bm.faces):
                bm.free()
                bpy.data.objects.remove(clone, do_unlink=True)
                bpy.data.meshes.remove(mesh_copy)
                continue

            bmesh.ops.delete(bm, geom=faces_to_delete, context="FACES")
            for face in bm.faces:
                face.material_index = 0
            bm.to_mesh(mesh_copy)
            bm.free()
            mesh_copy.update()

            material_name = material.name if material else f"Material_{material_index}"
            safe_material_name = material_name.replace(" ", "_")
            clone.name = f"{obj.name}_{safe_material_name}"

            mesh_copy.materials.clear()
            if material:
                mesh_copy.materials.append(material)

            created_objects.append(clone.name)

        if created_objects:
            original_mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if original_mesh.users == 0:
                bpy.data.meshes.remove(original_mesh)
            split_meshes.extend(created_objects)

    return split_meshes


def add_uma_bones():
    """
    Inserts 'Global' and 'Position' bones above the existing 'hip' bone,
    creating the UMA-required hierarchy: Global → Position → hip → (Daz skeleton).
    Also resets mesh rotation to zero.
    """
    _debug_log("add_uma_bones called")
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
    for obj in _iter_armatures_priority():
        bones = obj.data.bones
        # UMA rig: Global bone has Position as a child
        is_uma_for_obj = (
            "Global" in bones
            and any(child.name == "Position" for child in bones["Global"].children)
        )
        if is_uma_for_obj:
            result["is_uma_rig"] = True

        # Daz rig: 'hip' present, no CC4 bones, and not already UMA-converted.
        if not is_uma_for_obj and "hip" in bones and "CC_Base_Hip" not in bones:
            result["is_daz_rig"] = True
            generation = _detect_generation(obj)
            # Keep the first known generation so later armatures don't overwrite it.
            if result["generation"] == "unknown" and generation != "unknown":
                result["generation"] = generation
    return result

def meshes_to_overlay(mesh_names):
    """Takes a list of mesh names and returns a list of unique materials used by these meshes. Those are our overlays, UMA needs to generate them."""
    _debug_log(f"meshes_to_overlay called with mesh_names={mesh_names}")
    unique_materials = set()  # Ein Set, um Duplikate zu vermeiden
    for mesh_name in mesh_names:
        mesh = bpy.data.objects.get(mesh_name)
        if mesh and mesh.type == 'MESH':
            for mat_slot in mesh.material_slots:
                if mat_slot.material:  # Überprüfe, ob das Material existiert
                    unique_materials.add(mat_slot.material.name)
    return list(unique_materials)  # Konvertiere das Set zurück in eine Liste"""


def mesh_to_overlay(mesh_name):
    """Takes a mesh name and returns a single material name as string used by this mesh. Those are our overlay, UMA needs to generate them."""
    _debug_log(f"mesh_to_overlay called with mesh_name={mesh_name}")
    mesh = bpy.data.objects.get(mesh_name)
    if mesh and mesh.type == 'MESH':
        for mat_slot in mesh.material_slots:
            if mat_slot.material:  # Überprüfe, ob das Material existiert
                return mat_slot.material.name
    return ""  # Konvertiere das Set zurück in eine Liste"""


def find_textures_custom_path(base_path, search_pattern):
    pattern = os.path.join(base_path, "**", search_pattern)
    files = glob.glob(pattern, recursive=True)
    if not files and "." not in os.path.basename(search_pattern):
        fallback_pattern = os.path.join(base_path, "**", search_pattern + "*.*")
        files = glob.glob(fallback_pattern, recursive=True)
    return sorted(files)


def _pick_first_texture_file(files):
    valid_extensions = (".png", ".jpg", ".jpeg", ".tif", ".tiff")
    for file_path in files:
        if file_path.lower().endswith(valid_extensions):
            return file_path
    return None


def _find_first_texture_by_patterns(base_path, patterns):
    for pattern in patterns:
        found = find_textures_custom_path(base_path, pattern)
        chosen = _pick_first_texture_file(found)
        if chosen:
            return chosen
    return None


def _build_suffix_patterns(base_pattern, suffixes):
    patterns = []
    token = "_[0-9]*"
    for suffix in suffixes:
        if token in base_pattern:
            if "*" in suffix:
                patterns.append(base_pattern.replace(token, suffix))
            patterns.append(base_pattern.replace(token, f"{suffix}_[0-9]*"))
        else:
            patterns.append(base_pattern + suffix)
    deduped_patterns = []
    seen = set()
    for pattern in patterns:
        if pattern not in seen:
            deduped_patterns.append(pattern)
            seen.add(pattern)
    return deduped_patterns


def _resolve_color_map_patterns(base_texture_pattern):
    return [base_texture_pattern] + _build_suffix_patterns(
        base_texture_pattern, ["D", "*diffuse*"]
    )


def _resolve_additional_map_patterns(base_texture_pattern, map_config):
    if isinstance(map_config, str):
        return _build_suffix_patterns(base_texture_pattern, [map_config])

    if isinstance(map_config, (list, tuple)):
        return _build_suffix_patterns(base_texture_pattern, map_config)

    if isinstance(map_config, dict):
        pattern_base = map_config.get("base_pattern", base_texture_pattern)
        suffixes = map_config.get("suffixes", [])
        return _build_suffix_patterns(pattern_base, suffixes)

    return []


def _add_texture_to_material(material, texture_path, texture_type):
    _debug_log(
        f"_add_texture_to_material called with material={material.name}, texture_type={texture_type}, texture_path={texture_path}"
    )
    transparent_materials = {"Cornea", "EyeMoisture", "Eyelashes"}
    texture_locations = {
        "color": (-800, 300),
        "roughness": (-800, 0),
        "metallic": (-800, -200),
        "specular": (-800, -400),
        "bump": (-800, -600),
        "normal": (-800, -800),
    }

    def _clear_input_links(input_socket):
        for link in list(input_socket.links):
            links.remove(link)

    def _link_to_input(output_socket, input_socket):
        _clear_input_links(input_socket)
        links.new(output_socket, input_socket)

    # Ensure the material uses nodes
    if not material.use_nodes:
        material.use_nodes = True

    nodes = material.node_tree.nodes
    links = material.node_tree.links

    bsdf = next((node for node in nodes if node.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        bsdf = nodes.new(type='ShaderNodeBsdfPrincipled')
        bsdf.location = (0, 0)
        output = next((node for node in nodes if node.type == 'OUTPUT_MATERIAL'), None)
        if output is None:
            output = nodes.new(type='ShaderNodeOutputMaterial')
            output.location = (300, 0)
        if 'Surface' in output.inputs:
            _link_to_input(bsdf.outputs['BSDF'], output.inputs['Surface'])

    texture_node = nodes.get(texture_type)
    if texture_node is None or texture_node.type != 'TEX_IMAGE':
        texture_node = nodes.new(type='ShaderNodeTexImage')

    texture_node.image = bpy.data.images.load(texture_path, check_existing=True)
    texture_node.name = texture_node.label = texture_type
    texture_node.location = texture_locations.get(texture_type, (-800, 0))

    # Set colorspace for non-color data
    if texture_type in ['roughness', 'metallic', 'normal', 'specular', 'bump']:
        texture_node.image.colorspace_settings.name = 'Non-Color'
    else:
        texture_node.image.colorspace_settings.name = 'sRGB'

    if texture_type == 'color':
        _link_to_input(texture_node.outputs['Color'], bsdf.inputs['Base Color'])
        if material.name in transparent_materials and 'Alpha' in bsdf.inputs:
            _link_to_input(texture_node.outputs['Alpha'], bsdf.inputs['Alpha'])
            if hasattr(material, 'blend_method'):
                material.blend_method = 'BLEND'
            if hasattr(material, 'shadow_method'):
                material.shadow_method = 'HASHED'
    elif texture_type == 'metallic':
        _link_to_input(texture_node.outputs['Color'], bsdf.inputs['Metallic'])
    elif texture_type == 'roughness':
        _link_to_input(texture_node.outputs['Color'], bsdf.inputs['Roughness'])
    elif texture_type == 'specular':
        if 'Specular IOR Level' in bsdf.inputs:
            _link_to_input(texture_node.outputs['Color'], bsdf.inputs['Specular IOR Level'])
        elif 'Specular' in bsdf.inputs:
            _link_to_input(texture_node.outputs['Color'], bsdf.inputs['Specular'])
    elif texture_type == 'bump':
        bump_node = nodes.get('bump_converter')
        if bump_node is None or bump_node.type != 'BUMP':
            bump_node = nodes.new(type='ShaderNodeBump')
        bump_node.name = bump_node.label = 'bump_converter'
        bump_node.location = (-400, -600)
        _link_to_input(texture_node.outputs['Color'], bump_node.inputs['Height'])
        _link_to_input(bump_node.outputs['Normal'], bsdf.inputs['Normal'])
    elif texture_type == 'normal':
        normal_map_node = nodes.get('normal_converter')
        if normal_map_node is None or normal_map_node.type != 'NORMAL_MAP':
            normal_map_node = nodes.new(type='ShaderNodeNormalMap')
        normal_map_node.name = normal_map_node.label = 'normal_converter'
        normal_map_node.location = (-400, -800)
        _link_to_input(texture_node.outputs['Color'], normal_map_node.inputs['Color'])
        _link_to_input(normal_map_node.outputs['Normal'], bsdf.inputs['Normal'])



def setup_daz_materials(search_base_path):
    """
    Assign textures to materials using pattern-based matching from the mapping.
    Uses glob patterns to find textures for different character exports (flexible naming).
    Searches for color, roughness, metallic, specular, and bump textures.
    """
    _debug_log(f"setup_daz_materials called with search_base_path={search_base_path}")
    
    for material in bpy.data.materials:
        print(f"\nProcessing Material: {material.name}")
        
        if not material.use_nodes:
            print("  ✗ Material does not use nodes, skipping.")
            continue
        
        # Look up the material in the mapping
        texture_pattern = _MATERIAL_TEXTURE_MAP.get(material.name)
        if not texture_pattern:
            print(f"  ✗ No texture mapping found for material '{material.name}'")
            continue
        
        print(f"  → Texture pattern: {texture_pattern}")
        
        # Search for color texture (jpg or png) using the pattern
        color_patterns = _resolve_color_map_patterns(texture_pattern)
        color_file = _find_first_texture_by_patterns(search_base_path, color_patterns)
        
        if color_file:
            print(f"  ✓ Found color texture: {os.path.basename(color_file)}")
            _add_texture_to_material(material, color_file, 'color')
        else:
            print(f"  ✗ No color texture found for pattern '{texture_pattern}'")
        
        # Search additional known maps (roughness, metallic, specular, bump).
        additional_maps = _MATERIAL_ADDITIONAL_MAPS.get(material.name, {})
        for map_type, map_config in additional_maps.items():
            map_patterns = _resolve_additional_map_patterns(
                texture_pattern, map_config
            )
            map_file = _find_first_texture_by_patterns(search_base_path, map_patterns)
            if map_file:
                print(
                    f"  ✓ Found {map_type} texture: {os.path.basename(map_file)}"
                )
                _add_texture_to_material(material, map_file, map_type)

