import bpy
import importlib
import os
import glob

if "dataHandling" in locals():
    importlib.reload(dataHandling)
else:
    from . import dataHandling

def check_rig():
    """
    Detects if the current armature is a Daz Genesis rig, UMA rig, and determines generation.
    Returns:
        dict: {
            'is_daz_rig': bool,
            'is_uma_rig': bool,
            'generation': str
        }
    """
    armature = None
    for obj in bpy.context.selected_objects:
        if obj.type == 'ARMATURE':
            armature = obj
            break
    if not armature:
        for obj in bpy.context.scene.objects:
            if obj.type == 'ARMATURE':
                armature = obj
                break
    if not armature:
        return {'is_daz_rig': False, 'is_uma_rig': False, 'generation': 'unknown'}

    bones = armature.data.bones
    bone_names = set(bones.keys())

    # UMA rig detection
    is_uma_rig = False
    if 'Global' in bone_names:
        global_bone = bones['Global']
        children = [b.name for b in global_bone.children]
        if 'Position' in children:
            is_uma_rig = True

    # Daz rig detection
    is_daz_rig = False
    if 'hip' in bone_names and 'CC_Base_Hip' not in bone_names:
        is_daz_rig = True

    # Generation detection
    generation = 'unknown'
    if 'hip' in bone_names:
        hip_bone = bones['hip']
        hip_children = [b.name for b in hip_bone.children]
        if 'abdomen' in hip_children:
            generation = 'G3'
        elif 'abdomenLower' in hip_children:
            generation = 'G8'
        elif 'spine1' in hip_children:
            generation = 'G9'

    return {
        'is_daz_rig': is_daz_rig,
        'is_uma_rig': is_uma_rig,
        'generation': generation
    }
