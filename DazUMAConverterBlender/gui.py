import bpy

from bpy.props import StringProperty, CollectionProperty, BoolProperty, EnumProperty
from bpy.types import PropertyGroup

def wardrobe_slot_search(self, context, edit_text):
    slots = [
        ("None"),
        ("Face"),
        ("Hair"),
        ("Complexion"),
        ("Eyebrows"),
        ("Beard"),
        ("Ears"),
        ("Helmet"),
        ("Shoulders"),
        ("Chest"),
        ("Arms"),
        ("Hands"),
        ("Waist"),
        ("Legs"),
        ("Feet")
        # Fügen Sie hier weitere Slots hinzu...
    ]
    # Sie können hier Logik hinzufügen, um die Vorschläge basierend auf `edit_text` zu filtern
    return slots


# Property Group for Mesh Items
class MeshItem(PropertyGroup):
    ## General Properties
    selected: BoolProperty(name="Select", default=False)
    slot_name: StringProperty(name="Name", default="")
    ## Clothing Properties
    wardrobe_slot: StringProperty(name="Wardrobe Slot", default="None", search=wardrobe_slot_search,)
    overlay_name: StringProperty(name="Overlay Name", default="")
# Registration
    
def register_rig_type_selector():
    bpy.types.Scene.rig_type = bpy.props.EnumProperty(
        name="Rig Type",
        description="Select the type of rig you want to convert",
        items=[
            ('race', "Race", "A unclothed character which will be a new UMA Race"),
            ('clothing', "Clothing", "A Clothed character whichs clothing will be converted to UMA Slots"),
        ],
        default='race',
    )


def register_split_mode_selector():
    bpy.types.Scene.split_mode = bpy.props.EnumProperty(
        name="Split Mode",
        description="Choose how meshes should be prepared during conversion",
        items=[
            ('materials', "Separate by Materials", "Split each mesh into one object per material"),
            ('object', "By Object", "Keep imported mesh objects as they are"),
        ],
        default='materials',
    )
    
def unregister_rig_type_selector():
    del bpy.types.Scene.rig_type


def unregister_split_mode_selector():
    del bpy.types.Scene.split_mode

def register_json_file_field():
    bpy.types.Scene.json_file_path = bpy.props.StringProperty(
        name="JSON File Path",
        description="The file path for the JSON file to store the data in.",
        subtype='FILE_PATH',
    )


def unregister_json_file_field():
    del bpy.types.Scene.json_file_path


def register_race_wizard():
    # Name field for the Race
    bpy.types.Scene.race_name = bpy.props.StringProperty(
        name="Race Name",
        description="The name of the new UMA Race",
        default="NewRace"
    )

def unregister_race_wizard():
    del bpy.types.Scene.race_name


def register_mesh_items():
    bpy.utils.register_class(MeshItem)
    bpy.types.Scene.mesh_items = CollectionProperty(type=MeshItem)

def unregister_mesh_items():
    bpy.utils.unregister_class(MeshItem)
    del bpy.types.Scene.mesh_items


def register_select_all():
    bpy.types.Scene.select_all_meshes = bpy.props.BoolProperty(
        name="Select All Meshes",
        description="Select or deselect all available meshes for export",
        default=True
    )


def unregister_select_all():
    del bpy.types.Scene.select_all_meshes


def register_batch_rename_pattern():
    bpy.types.Scene.batch_rename_pattern = bpy.props.StringProperty(
        name="Rename Pattern",
        description="Pattern for batch renaming slots. Use {mesh} for mesh name placeholder",
        default="{mesh}"
    )
    
    bpy.types.Scene.batch_rename_mode = bpy.props.EnumProperty(
        name="Rename Mode",
        description="Choose how to rename slots",
        items=[
            ('simple', "Simple Pattern", "Use {mesh} placeholder in pattern"),
            ('search_replace', "Search & Replace", "Find text and replace it"),
            ('delete', "Delete Pattern", "Remove matching text from slot name"),
        ],
        default='simple'
    )
    
    bpy.types.Scene.batch_search_text = bpy.props.StringProperty(
        name="Search Text",
        description="Text to search for in slot names",
        default=""
    )
    
    bpy.types.Scene.batch_replace_text = bpy.props.StringProperty(
        name="Replace Text",
        description="Text to replace with (leave empty to delete)",
        default=""
    )


def unregister_batch_rename_pattern():
    del bpy.types.Scene.batch_rename_pattern
    del bpy.types.Scene.batch_rename_mode
    del bpy.types.Scene.batch_search_text
    del bpy.types.Scene.batch_replace_text