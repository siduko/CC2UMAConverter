# 🩻 Character Creator 4 & Daz3D To UMA [ Experimental ]
## 📖Description
This set of plugins for Blender and Unity converts characters to fully functional, ready-to-use UMA assets. It supports both **Character Creator 4** and **Daz3D Genesis** (Gen3, Gen8, Gen8.1, Gen9) as source formats, and shares a single **Unity post-processor** — so assets from either source drop straight into the same UMA project.

The plugins handle Race creation (naked character → new UMA Race) and Wardrobe Slot creation (clothed character → UMA clothing). They take care of the necessary conversion steps and keep everything as simple as possible, letting you focus on preparing your character in CC4 or Daz Studio and building an easy bridge to UMA. While you can modify the resulting UMA assets directly in Unity, the plugins are designed to let you handle everything in Blender.

> **Two Blender plugins, one Unity plugin:**
> - `UMAConverterBlender` — for Character Creator 4
> - `DazUMAConverterBlender` — for Daz3D Genesis
> - `UmaConverterUnity` — shared Unity post-processor (works with both)

---

# 🩻 Character Creator 4 [ Experimental ]

## 🎮Unity Plugin
A shared Unity Asset Postprocessor handles the output from **both** Blender plugins (CC4 and Daz). It reads the exported FBX and JSON files and automatically creates races and wardrobe slots for you, doing all the heavy lifting in the background.

## 📺See it in Action
https://imgur.com/a/tKXUXQj



## 🛠️Installation
### Blender Plugin
1. Download the latest release from the [Releases](https://github.com/valentinwinkelmann/CC2UMAConverter/releases) page.
2. Open Blender and go to Edit > Preferences > Add-ons > Install
3. Select the downloaded zip file and click Install Add-on
4. Enable the Add-on by checking the box next to it
5. Click File > Defaults > Save Startup File to make sure the Add-on is enabled by default

### Unity Plugin
Simply open your Unity Package Manager and click on the + Button in the top left corner. Choose "Add package from git URL" and paste the following URL:

``https://github.com/valentinwinkelmann/CC2UMAConverter.git?path=UmaConverterUnity``

That's it. The Plugin will be installed and ready to use.

## 🎛️Usage

### 📦 Character Creator 4 Export
1. `File > Export > Clothed Character`
2. Set **Target Tool Preset** to `Maya` *(not Unity)*
3. Set **FBX Options** to `Mesh`
4. Disable **Embed Textures**
5. Enable **InstaLOD Material Merge** (choose the type you want)

### 🩻 Create a new UMA Race
1. In Blender open the **UMA Converter** panel (N-panel, `UMA Converter` tab).
2. Click **Import FBX** and select your exported CC4 character FBX.
3. Set **Rig Type** to `Race` and press **Convert**.
4. Select the meshes to export and fill in a unique **Slot Name** for each.
5. Rename materials in the Blender Material tab if needed — these become your UMA Overlay names.
6. Press **Export Selected**, choose a save location.
7. Keep the `_race.json` file alongside the FBX — you'll need it for clothing exports.
8. Drag both the `.fbx` and `_race.json` into your Unity project.

### 👕 Create UMA Clothing
1. Import a clothed CC4 character FBX.
2. Set **Rig Type** to `Clothing` and select the `_race.json` from your previously exported Race.
3. Press **Convert**, then select the clothing meshes (race meshes are automatically ignored).
4. Fill in Slot Names, rename materials as needed, and set a **Wardrobe Slot Type** for each mesh.
5. Press **Export Selected**.
6. Drag the `.fbx` and `_cloth.json` into Unity.

### ⚠️ Important Notes and Limitations
- If you export clothing for a race, you must use exactly the same base character for all clothing exports. Save your Race Template Character in CC4 for future use.
- Only PBR materials are supported. Set the Material Type to PBR in CC4 before exporting.
- Only character exports using InstaLOD Material Merge are supported.
- Mesh edits in Blender may break the scene and cause unexpected results.
- Hats and non-skinned accessories with extra bones are not supported. Skin them like regular clothing to export.

## 🔮Planned and Upcoming Features
- [X] Race Conversion
- [X] Wardrobe Slot Conversion
- [X] Define Wardrobe Slot Types in Blender
- [X] Renaming Slots and Material Names in Blender
- [X] Export Textures and rename them as their Overlay Names
- [X] Unity Postprocessor to Create all UMA Assets for you
- [x] GameCreator 2 Integration
- [X] Daz3D Genesis support (Gen3/8/8.1/9)
- [ ] Rework the Codebase to be more flexible and extendable
- [ ] Make Renaming the Materials automatically in Blender
- [ ] Thumbnail Export for UMA Wardrobe Slots
- [ ] Individual Wardrobe Slot types for each Race
- [ ] Stability and Performance Improvements
- [ ] Support all Render Pipelines
- [ ] Support non PBR Workflows like CC4's Skin, Eye and Hair Materials
- [ ] Support Mesh exports without InstaLOD Material Merge
- [ ] Support for Accessories and Hats without Skinning


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

## 📜License
This Blender Plugin and Unity Plugin are Developed and Copyrighted by [Valentin Winkelmann](https://vwgame.dev/). This Software is Free to use in a Non-Commercial and Commercial Enviroment. Please read the full [End-User License Agreement](https://github.com/valentinwinkelmann/CC2UMAConverter/blob/main/license.md)
