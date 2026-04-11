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

### 📦Character Creator 4 Export
Exporting your Character from Character Creator 4 is pretty simple, but needs to follow a few very important steps, so the Blender Plugin can understant it's structure.
1. ```File > Export > Clothed Character```
2. Set Target Tool Preset to ```Maya```. (*Not Unity*)
3. Set FBX Options to ```Mesh```.
4. Disable Embed Textures.
5. Enable InstaLOD Material Merge ( the type you want )

### 🩻Create a new UMA Race
To convert a naked CC4 Character to a new UMA Race, you will import the character in blender and Choose Rig Type: Race. When you press converting the Plugin will calculate some information about the rig in Background.
Inside the UMA Tab you will now be able to select which of the meshes should be exported. When you select one you will see a Slot Name field for the selected mesh. You have to fill this and it should be unique for each mesh. This will be the UMA Slot Name in Unity.
You Will Also see Available Overlays list of Material names. This materials are the one which UMA will Create and are defined by the Blender Material Names. You can Rename them using the default Blender Material Tab.

Use the Export button to Choose a location for your Character. Along your exported FBX file you will find a JSON file that contains some information about your character.
This JSON file is important when you want to convert new Clothings for your Character, so keep it safe.
You can now Drag and Drop booth files into your Unity Project and the Postprocessor will take care of the rest.

### 👕Create a new UMA Clothing
To convert a clothed CC4 Character to a new UMA Wardrobe Slot, you will import the character in blender and Choose Rig Type: Clothing. Before you can press the convert button you have to select a Race JSON File, that you created before.
Now you can press the convert button and the Plugin will do the rest. Like the UMA Race workflow you can now choose which meshes should be exported ( Race Meshes will be ignored and while they are in the Scene you dont have to worry about them ). As with the Race Creation you will have to fill the Slot Names and can rename the Material Names to your liking. You also have to Choose your desired Wardrobe Slot Type. The Plugin will give you a predefined list of Wardrobe Slots which UMA uses by Default but you can freely type any Wardrobe Slot Type you want, just make sure this is consistent with your other Clothing conversions.
Aft. You will find a JSON file next to the FBX file that contains some information about your clothing. This is the *_Cloth.json file.

### ⚠️Important Notes and Limitations
- If you Export clothing for a Race you created before, you have to use exactly the same Character for the Clothing Exports. If you plan to export a set of multiple clothings over time, you should save your Race Template Character in Character Creator 4.
- The Plugin Exports JSON Files, which are simple but better keep them if you plan to create new clothings for your character in the future.
- The Plugin is not meant to let you modifiy and adjust the Mesh in Blender. You can do that, but it may break the scene and make the Plugin not work as expected with your modified meshes.
- The Plugin works currently only with PBR Materials. before Exporting your Character from Character Creator 4, it's therefore necessary to set the Material Type to PBR. At the moment there are no plans to extend the Plugin to handle other Material Types.
- Currently the Plugin only works with Character Exports which used InstaLOD Material Merge.
- Currently you can't export a Character with Hats or other non skinned Accessories. CC4 adds them with a Nasty Extra Bone and the Plugin won't handle that. If you Want to Export a Hat, you have to skin it like a normal Clothing Piece and bind it to the Head Bone.

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
