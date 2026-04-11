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
