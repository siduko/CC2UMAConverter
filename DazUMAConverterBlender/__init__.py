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
import re
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


def refresh_mesh_items(context):
    context.scene.mesh_items.clear()
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            item = context.scene.mesh_items.add()
            item.name = obj.name
            item.slot_name = obj.name


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
            layout.prop(context.scene, "split_mode", text="Mesh Mode")

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
            
            # Select All checkbox and button
            row = layout.row(align=True)
            row.prop(context.scene, "select_all_meshes", text="Select All")
            row.operator("dazuma.select_all_meshes", text="Apply")
            
            # Display mesh items
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
            
            # Batch Rename Slots
            layout.separator()
            box = layout.box()
            box.label(text="Batch Rename Slots", icon="SORTALPHA")
            box.prop(context.scene, "batch_rename_mode", text="Mode")
            
            # Show fields based on selected mode
            if context.scene.batch_rename_mode == 'simple':
                box.prop(context.scene, "batch_rename_pattern", text="Pattern")
                box.label(text="Use {mesh} as placeholder", icon="INFO")
            elif context.scene.batch_rename_mode == 'search_replace':
                box.prop(context.scene, "batch_search_text", text="Find")
                box.prop(context.scene, "batch_replace_text", text="Replace")
            elif context.scene.batch_rename_mode == 'delete':
                box.prop(context.scene, "batch_search_text", text="Text to Delete")
            
            box.operator("dazuma.batch_rename_slots", text="Apply Batch Rename")

            layout.operator("dazuma.export", text="Export Selected")

        else:
            layout.label(text="Please import a Daz Genesis FBX", icon="INFO")
            box = layout.box()
            box.label(text="Import Options")
            box.prop(context.scene, "import_skip_manual_mapping", text="Skip Manual Mapping")
            import_op = box.operator("dazuma.import", text="Import FBX")
            import_op.skip_manual_mapping = context.scene.import_skip_manual_mapping


# ── Operators ────────────────────────────────────────────────────────────────

class DAZUMA_OT_Convert(Operator):
    bl_idname = "dazuma.convert"
    bl_label = "Convert"

    def execute(self, context):
        init_blender_viewport()
        dazconverter.apply_transforms_rest_pose()
        race_data = None

        if context.scene.rig_type == "race":
            hip_height = dazconverter.get_daz_hip_height_global()
            if hip_height is None:
                self.report({"ERROR"}, "Could not find hip bone. Ensure the armature has a 'hip' bone.")
                return {"CANCELLED"}
            race_data = dataHandling.UMAData_Race("NewRace", hip_height, [], [], [])
            dataHandling.save_to_scene_properties(race_data, "race_data")

        if context.scene.rig_type == "clothing":
            race_data = dataHandling.load_from_json_file(
                context.scene.json_file_path, dataHandling.UMAData_Race
            )
            dataHandling.save_to_scene_properties(race_data, "race_data")
            current_hip = dazconverter.get_daz_hip_height_global()
            if current_hip is None:
                self.report({"ERROR"}, "Could not find hip bone for height adjustment.")
                return {"CANCELLED"}
            difference = current_hip - race_data.hipHeight
            dazconverter.adjust_daz_hip_height(-difference)

        dazconverter.add_uma_bones()

        excluded_meshes = race_data.meshes if context.scene.rig_type == "clothing" and race_data is not None else []
        if context.scene.split_mode == "materials":
            dazconverter.split_meshes_by_material(excluded_meshes)

        refresh_mesh_items(context)
        self.report({"INFO"}, "Conversion complete.")
        return {"FINISHED"}


class DAZUMA_OT_Import(Operator, ImportHelper):
    bl_idname = "dazuma.import"
    bl_label = "Import FBX"
    filename_ext = ".fbx"
    filter_glob: StringProperty(default="*.fbx", options={"HIDDEN"})
    skip_manual_mapping: BoolProperty(
        name="Skip Manual Mapping",
        description="Do not show manual mapping dialog before automatic texture setup",
        default=False,
    )

    def draw(self, context):
        self.layout.prop(self, "skip_manual_mapping")

    def execute(self, context):
        import_options = {
            "use_anim": False,
            "ignore_leaf_bones": True,
            "automatic_bone_orientation": False,
        }
        bpy.ops.import_scene.fbx(filepath=self.filepath, **import_options)
        refresh_mesh_items(context)

        scene = context.scene
        file_dir = os.path.splitext(self.filepath)[0] + ".images"

        scene.pending_texture_setup_path = file_dir
        if self.skip_manual_mapping:
            dazconverter.setup_daz_materials(
                file_dir,
                skip_manual_mapping=True,
            )
            scene.pending_texture_setup_path = ""
            self.report({"INFO"}, "FBX imported successfully.")
            return {"FINISHED"}

        candidate = dazconverter.get_manual_mapping_candidate()
        if candidate is None:
            dazconverter.setup_daz_materials(
                file_dir,
                skip_manual_mapping=False,
            )
            scene.pending_texture_setup_path = ""
            self.report({"INFO"}, "FBX imported successfully.")
            return {"FINISHED"}

        scene.manual_texture_material_name = candidate["material_name"]
        scene.manual_texture_path = candidate["texture_path"]
        scene.manual_texture_type = candidate["texture_type"]
        scene.manual_texture_error = "Choose a mapping before automatic setup runs."
        bpy.ops.dazuma.manual_texture_mapping("INVOKE_DEFAULT")
        self.report({"INFO"}, "FBX imported successfully.")
        return {"FINISHED"}


class DAZUMA_OT_ManualTextureMapping(Operator):
    bl_idname = "dazuma.manual_texture_mapping"
    bl_label = "Manual Texture Mapping"

    _TEXTURE_TYPES = [
        ("color", "Color", "Connect to Base Color"),
        ("roughness", "Roughness", "Connect to Roughness"),
        ("metallic", "Metallic", "Connect to Metallic"),
        ("specular", "Specular", "Connect to Specular"),
        ("bump", "Bump", "Connect through Bump node"),
        ("normal", "Normal", "Connect through Normal Map node"),
        ("transparency", "Transparency", "Connect to Alpha"),
    ]

    @classmethod
    def poll(cls, context):
        return hasattr(context.scene, "manual_texture_material_name")

    def invoke(self, context, event):
        return context.window_manager.invoke_props_dialog(self, width=520)

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        layout.label(text="Automatic texture mapping failed.", icon="ERROR")
        if scene.manual_texture_error:
            layout.label(text=f"Reason: {scene.manual_texture_error}")
        layout.label(text=f"Material: {scene.manual_texture_material_name}")
        layout.prop(scene, "manual_texture_path", text="Texture Path")
        layout.prop(scene, "manual_texture_type", text="Texture Type")

    def execute(self, context):
        scene = context.scene
        material = bpy.data.materials.get(scene.manual_texture_material_name)
        if material is None:
            self.report(
                {"ERROR"},
                f"Material '{scene.manual_texture_material_name}' not found.",
            )
            return {"CANCELLED"}

        texture_path = bpy.path.abspath(scene.manual_texture_path)
        if not texture_path or not os.path.isfile(texture_path):
            self.report({"ERROR"}, "Texture path does not exist.")
            return {"CANCELLED"}

        try:
            dazconverter._add_texture_to_material(
                material,
                texture_path,
                scene.manual_texture_type,
            )
        except Exception as exc:
            self.report({"ERROR"}, f"Manual mapping failed: {exc}")
            return {"CANCELLED"}

        self.report(
            {"INFO"},
            f"Mapped {os.path.basename(texture_path)} as {scene.manual_texture_type} for {material.name}",
        )

        if scene.pending_texture_setup_path:
            dazconverter.setup_daz_materials(
                scene.pending_texture_setup_path,
                skip_manual_mapping=True,
            )
            scene.pending_texture_setup_path = ""
        return {"FINISHED"}

    def cancel(self, context):
        context.scene.pending_texture_setup_path = ""


class DAZUMA_OT_SelectAllMeshes(Operator):
    bl_idname = "dazuma.select_all_meshes"
    bl_label = "Select All Meshes"
    
    def execute(self, context):
        select_state = context.scene.select_all_meshes
        for item in context.scene.mesh_items:
            item.selected = select_state
        return {"FINISHED"}


class DAZUMA_OT_BatchRenameSlots(Operator):
    bl_idname = "dazuma.batch_rename_slots"
    bl_label = "Batch Rename Slots"
    
    def execute(self, context):
        rename_mode = context.scene.batch_rename_mode
        renamed_count = 0
        
        for item in context.scene.mesh_items:
            if item.selected:
                old_name = item.slot_name
                
                if rename_mode == 'simple':
                    # Simple pattern mode: replace {mesh} placeholder
                    pattern = context.scene.batch_rename_pattern
                    new_slot_name = pattern.replace("{mesh}", item.name)
                    
                elif rename_mode == 'search_replace':
                    # Search and replace mode
                    search_text = context.scene.batch_search_text
                    replace_text = context.scene.batch_replace_text
                    
                    if search_text:
                        new_slot_name = item.slot_name.replace(search_text, replace_text)
                    else:
                        self.report({"WARNING"}, "Search text is empty")
                        continue
                        
                elif rename_mode == 'delete':
                    # Delete pattern mode: remove text matching search
                    search_text = context.scene.batch_search_text
                    
                    if search_text:
                        new_slot_name = item.slot_name.replace(search_text, "")
                    else:
                        self.report({"WARNING"}, "Search text is empty")
                        continue
                else:
                    continue
                
                if new_slot_name != old_name:
                    item.slot_name = new_slot_name
                    renamed_count += 1
        
        mode_label = {
            'simple': 'pattern substitution',
            'search_replace': 'search and replace',
            'delete': 'deletion'
        }.get(rename_mode, 'unknown')
        
        self.report({"INFO"}, f"Renamed {renamed_count} slot(s) using {mode_label}")
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
        armature_found = False
        for obj in bpy.data.objects:
            if obj.type == "ARMATURE":
                obj.select_set(True)
                context.view_layer.objects.active = obj
                armature_found = True
                break
        if not armature_found:
            self.report({"ERROR"}, "No armature found. Cannot export without skeleton.")
            return {"CANCELLED"}

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
                    material_overlay = dazconverter.mesh_to_overlay(item.name)
                    slot = dataHandling.UMAData_Slot(
                        item.slot_name,
                        item.name,
                        material_overlay,
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
            selected_objects = [bpy.data.objects[item.name] for item in context.scene.mesh_items if item.selected]
            # Ruft die Funktion zum Speichern der Texturen auf
            save_textures_with_export(self.filepath, selected_objects, filename_no_ext)

        self.report({"INFO"}, "Export successful.")
        return {"FINISHED"}

    def draw(self, context):
        self.layout.prop(self, "export_textures")


# ── Texture export helper ────────────────────────────────────────────────────

def _normalize_channel_key(channel_name):
    normalized = (channel_name or "").strip().lower()
    return re.sub(r"[^a-z0-9]+", "", normalized)


def _get_export_channel_name(node_name):
    normalized_name = _normalize_channel_key(node_name)
    channel_map = {
        # Diffuse / Albedo family
        "color": "Diffuse",
        "basecolor": "Diffuse",
        "albedo": "Diffuse",
        "diffuse": "Diffuse",
        "basemap": "Diffuse",
        "maintex": "Diffuse",

        # Normal family
        "normalmap": "Normal",
        "normal": "Normal",
        "metallic": "metallic",
        "metalness": "metallic",

        # Roughness / Smoothness family
        "roughness": "roughness",
        "smoothness": "roughness",
        "glossiness": "roughness",

        # Bump / Height family
        "bump": "BumpMap",
        "bumpmap": "BumpMap",
        "bump_converter": "BumpMap",

        # Transparency / Alpha family
        "transparency": "TransparencyMap",
        "alpha": "TransparencyMap",
        "opacity": "TransparencyMap",

        # Other common texture channels used in the Unity material
        "metallicglossmap": "MetallicGlossMap",
        "detailalbedomap": "DetailAlbedoMap",
        "detailnormalmap": "DetailNormalMap",
        "detailmask": "DetailMask",
        "emission": "EmissionMap",
        "emissive": "EmissionMap",
        "emissionmap": "EmissionMap",
        "occlusion": "OcclusionMap",
        "ao": "OcclusionMap",
        "ambientocclusion": "OcclusionMap",
        "occlusionmap": "OcclusionMap",
        "parallax": "ParallaxMap",
        "height": "ParallaxMap",
        "displacement": "ParallaxMap",
        "parallaxmap": "ParallaxMap",
        "specular": "SpecGlossMap",
        "specgloss": "SpecGlossMap",
        "speculargloss": "SpecGlossMap",
        "specglossmap": "SpecGlossMap",
        "base": "BaseMap",
        "diffusemap": "Diffuse",
    }
    return channel_map.get(normalized_name)


def save_textures_with_export(filepath, selected_objects, custom_folder_name="Exported_Textures"):
    base_directory = os.path.dirname(filepath)
    custom_directory = os.path.join(base_directory, custom_folder_name, "Textures")
    os.makedirs(custom_directory, exist_ok=True)
    copied_targets = set()
    
    for obj in selected_objects:
        if obj.type == 'MESH' and obj.material_slots:
            for slot in obj.material_slots:
                if slot.material and slot.material.use_nodes:
                    for node in slot.material.node_tree.nodes:
                        if node.type == 'TEX_IMAGE' and node.image:
                            channel_name = _get_export_channel_name(node.name)
                            if channel_name is None:
                                continue

                            original_texture_path = bpy.path.abspath(node.image.filepath)
                            if os.path.isfile(original_texture_path):
                                _, extension = os.path.splitext(original_texture_path)
                                new_texture_name = f"{slot.material.name}_{channel_name}{extension}"
                                target_path = os.path.join(custom_directory, new_texture_name)
                                if target_path in copied_targets:
                                    continue
                                shutil.copy2(original_texture_path, target_path)
                                copied_targets.add(target_path)
                                print(f"Texture copied from {original_texture_path} to {target_path}")


# ── Register / Unregister ────────────────────────────────────────────────────

def register():
    gui.register_rig_type_selector()
    gui.register_split_mode_selector()
    gui.register_json_file_field()
    gui.register_import_options()
    gui.register_race_wizard()
    gui.register_mesh_items()
    gui.register_select_all()
    gui.register_batch_rename_pattern()
    bpy.utils.register_class(DAZUMA_PT_Panel)
    bpy.utils.register_class(DAZUMA_OT_Convert)
    bpy.utils.register_class(DAZUMA_OT_Import)
    bpy.types.Scene.manual_texture_material_name = StringProperty(
        name="Manual Material",
        default="",
        options={"HIDDEN"},
    )
    bpy.types.Scene.manual_texture_path = StringProperty(
        name="Texture Path",
        description="Texture file path to map manually",
        subtype="FILE_PATH",
        default="",
    )
    bpy.types.Scene.manual_texture_type = EnumProperty(
        name="Texture Type",
        description="Destination texture input type",
        items=DAZUMA_OT_ManualTextureMapping._TEXTURE_TYPES,
        default="color",
    )
    bpy.types.Scene.manual_texture_error = StringProperty(
        name="Texture Error",
        default="",
        options={"HIDDEN"},
    )
    bpy.types.Scene.pending_texture_setup_path = StringProperty(
        name="Pending Texture Setup Path",
        default="",
        options={"HIDDEN"},
    )
    bpy.utils.register_class(DAZUMA_OT_ManualTextureMapping)
    bpy.utils.register_class(DAZUMA_OT_SelectAllMeshes)
    bpy.utils.register_class(DAZUMA_OT_BatchRenameSlots)
    bpy.utils.register_class(DAZUMA_OT_Export)


def unregister():
    gui.unregister_rig_type_selector()
    gui.unregister_split_mode_selector()
    gui.unregister_json_file_field()
    gui.unregister_import_options()
    gui.unregister_race_wizard()
    gui.unregister_mesh_items()
    gui.unregister_select_all()
    gui.unregister_batch_rename_pattern()
    bpy.utils.unregister_class(DAZUMA_PT_Panel)
    bpy.utils.unregister_class(DAZUMA_OT_Convert)
    bpy.utils.unregister_class(DAZUMA_OT_Import)
    bpy.utils.unregister_class(DAZUMA_OT_ManualTextureMapping)
    del bpy.types.Scene.manual_texture_material_name
    del bpy.types.Scene.manual_texture_path
    del bpy.types.Scene.manual_texture_type
    del bpy.types.Scene.manual_texture_error
    del bpy.types.Scene.pending_texture_setup_path
    bpy.utils.unregister_class(DAZUMA_OT_SelectAllMeshes)
    bpy.utils.unregister_class(DAZUMA_OT_BatchRenameSlots)
    bpy.utils.unregister_class(DAZUMA_OT_Export)


if __name__ == "__main__":
    register()
