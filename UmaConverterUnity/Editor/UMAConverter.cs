using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using UMA;
using UMA.Editors;
using UMA.CharacterSystem;
using UMAConverter;

namespace UMAConverter
{
    /// <summary>
    /// UMAConverter takes a single .fbx file and looks for a UMAData_<T>.json file in the same directory.
    /// It will then convert the .fbx into a set of UMA compatible assets.
    /// </summary>
    /// <typeparam name="T">The Type of UMAConverting Data, Either UMAData_Race or UMAData_Cloth</typeparam>
    public class UMAConverter<T> where T : IUMAData
    {
        string meshPath; // The path to the mesh file, like a .fbx
        string jsonPath; // The path to the UMAData_<T>.json file
        T data;
        string workingDirectory = null; // The directory in which we create our folder structure, is null as long no folder structure was created. Add / and the desired subfolder to get the full path.

        private List<UMAData_RaceSlots> raceSlots = new List<UMAData_RaceSlots>(); // We keep track of all Slots + Overlays

        GameObject model = null; // The imported model, we are taking our meshes from.

        private bool addToGlobalLibrary = true; // If true, the created assets will be added to the global library.

        /// <summary>
        /// Initializes a new instance of the <see cref="T:UMAConverter"/> class.
        /// A Mesh which has to be converted into UMA compatible assets needs to have a UMAData_<T>.json file in the same directory.
        /// It also have to pass the UMAConverter_PostProcessor first to be prepared for the conversion.
        /// </summary>
        /// <param name="MeshPath">The Path to our Mesh File, like a .fbx.</param>
        /// <exception cref="System.Exception"></exception>
        public UMAConverter(string MeshPath)
        {
            if (!System.IO.File.Exists(MeshPath)) throw new System.Exception("Mesh file not found at " + MeshPath);
            this.meshPath = MeshPath;
            string jsonPathSuffix = typeof(T).Name.ToLower().Split('_')[1];
            jsonPath = MeshPath.Replace(".fbx", "_" + jsonPathSuffix + ".json");
            this.data = JsonConvert.DeserializeObject<T>(System.IO.File.ReadAllText(jsonPath));
            if (this.data == null) throw new System.Exception("UMAData not found for " + jsonPath);

            // Load the model
            this.model = AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath);
            if(this.model == null) throw new System.Exception("Model not found at " + MeshPath);

        }


        /// <summary>
        /// Creates the folder structure for the UMA assets:
        /// - [name].fbx
        /// - [name]_[typeSuffix].json
        /// - [name]
        /// -- Overlays
        /// -- Slots
        /// -- Textures
        /// -- TPose (if IUMAData.type == race)
        /// -- Race (if IUMAData.type == race)
        /// -- Wardrobe (if IUMAData.type = cloth)
        /// </summary>
        private void CreateFolderStructure()
        {
            string folderPath = Path.GetDirectoryName(meshPath) + "/" + Path.GetFileNameWithoutExtension(meshPath);
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string[] subFolders = new string[] { "Overlays", "Slots", "Textures" };
            foreach (string subFolder in subFolders)
            {
                if (!Directory.Exists(folderPath + "/" + subFolder)) Directory.CreateDirectory(folderPath + "/" + subFolder);

            }
            if (this.data.type == UMADataType.cloth)
            {
                if (!Directory.Exists(folderPath + "/Wardrobe")) Directory.CreateDirectory(folderPath + "/Wardrobe");
#if UMAConverterGCInventory
                if (UMAConverterSettings.Instance.CreateItems)
                {
                    if (!Directory.Exists(folderPath + "/Items")) Directory.CreateDirectory(folderPath + "/Items");
                }
#endif
            }
            else
            {
                if (!Directory.Exists(folderPath + "/TPose")) Directory.CreateDirectory(folderPath + "/TPose");
                if (!Directory.Exists(folderPath + "/Race")) Directory.CreateDirectory(folderPath + "/Race");
            }
            workingDirectory = folderPath;

            foreach (UMAData_Slot slot in data.slots)
            {
                // Each slot has a Folder in the Slots directory
                string slotPath = workingDirectory + "/Slots/" + slot.name;
                if (!Directory.Exists(slotPath)) Directory.CreateDirectory(slotPath);
            }


            AssetDatabase.Refresh();

        }

        /// <summary>
        /// This method generates the SlotDataAsset for the given slot.
        /// It will automatically generate the OverlayDataAsset and keep track 
        /// </summary>
        /// <param name="slot"></param>
        /// <returns></returns>
        public SlotDataAsset GenerateSlotAsset(UMAData_Slot slot)
        {

            // ----------------- Slot -----------------
            string slotFolder = workingDirectory + "/Slots"; // General Slot folder, not the folder for the specific slot
            string assetFolder = ""; // Acording to reverse engineering it is empty
            string assetName = slot.name; // The Name of the Slot
            string slotName = slot.name; // The Name of the Slot
            bool nameByMaterial = false; // We are not using the material name as the asset name
            SkinnedMeshRenderer slotMesh = model.transform.Find(slot.mesh).GetComponent<SkinnedMeshRenderer>(); // The skinned mesh renderer for the slot
            UMAMaterial material = UMAConverterSettings.Instance.defaultMaterial; // TODO: Implement a way to decide which UMAMaterial should be used as default
            SkinnedMeshRenderer seamsMesh = null; //TODO: Implement a way that our Blender Plugin exports a seams mesh and tag it in the json, if a slot has seams
            List<string> keepBoneNames = new List<string>(); // The bones which should be kept, we are not using this feature
            string rootBone = "Global"; // Its by default "Global" and there is currently no need to change it
            bool binarySerialization = false; // We are not using binary serialization
            bool calcTangents = true; // We are calculating tangents by default
            string stripBones = ""; // We are not stripping bones

            if (slotMesh != null && slotMesh.sharedMaterials != null && slotMesh.sharedMaterials.Length > 1)
            {
                Debug.LogWarning("[UMAConverter] Slot '" + slot.name + "' uses " + slotMesh.sharedMaterials.Length + " source materials but only one overlay is currently exported ('" + slot.overlay + "'). This can cause wrong overlays (for example teeth textures on body).");
            }

            List<string> sourceMaterialNames = new List<string>();
            if (slotMesh != null && slotMesh.sharedMaterials != null)
            {
                foreach (Material sourceMaterial in slotMesh.sharedMaterials)
                {
                    sourceMaterialNames.Add(sourceMaterial != null ? sourceMaterial.name : "<null>");
                }
            }

            Debug.Log("[UMAConverter] Slot '" + slot.name + "' source materials: count=" + sourceMaterialNames.Count + ", names=[" + string.Join(",", sourceMaterialNames) + "], primaryOverlay='" + slot.overlay + "'.");

            if (slotMesh != null && slotMesh.sharedMesh != null)
            {
                int totalTriangles = 0;
                for (int subMeshIndex = 0; subMeshIndex < slotMesh.sharedMesh.subMeshCount; subMeshIndex++)
                {
                    totalTriangles += (int)slotMesh.sharedMesh.GetIndexCount(subMeshIndex) / 3;
                }

                Debug.Log("[UMAConverter] Slot '" + slot.name + "' mesh stats: mesh='" + slotMesh.sharedMesh.name + "', vertices=" + slotMesh.sharedMesh.vertexCount + ", subMeshes=" + slotMesh.sharedMesh.subMeshCount + ", triangles=" + totalTriangles + ".");
            }

            
            SlotBuilderParameters slotBuilderParameters = new SlotBuilderParameters();
            slotBuilderParameters.slotFolder = slotFolder;
            slotBuilderParameters.assetFolder = assetFolder;
            slotBuilderParameters.assetName = assetName;
            slotBuilderParameters.slotName = slotName;
            slotBuilderParameters.nameByMaterial = nameByMaterial;
            slotBuilderParameters.slotMesh = slotMesh;
            slotBuilderParameters.material = material;
            slotBuilderParameters.seamsMesh = seamsMesh;
            slotBuilderParameters.keepList = keepBoneNames;
            slotBuilderParameters.rootBone = rootBone;
            slotBuilderParameters.binarySerialization = binarySerialization;
            slotBuilderParameters.calculateTangents = calcTangents;
            slotBuilderParameters.stripBones = stripBones;
            //public static SlotDataAsset CreateSlotData(string slotFolder, string assetFolder, string assetName, string slotName, bool nameByMaterial, SkinnedMeshRenderer slotMesh, UMAMaterial material, SkinnedMeshRenderer seamsMesh, List<string> KeepList, string rootBone, bool binarySerialization = false, bool calcTangents = true, string stripBones = "", bool useRootFolder = false, bool adustForUDIM)
            SlotDataAsset slotAsset = UMASlotProcessingUtil.CreateSlotData(slotBuilderParameters);

            slotAsset.tags = new string[0]; // Currently we are not using tags
            UMAUpdateProcessor.UpdateSlot(slotAsset);

            if (addToGlobalLibrary)
            {
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), slotAsset);
            }

            // ----------------- Overlay -----------------

            OverlayDataAsset overlayAsset = null;
            List<OverlayDataAsset> overlaysForSlot = new List<OverlayDataAsset>();

            if (!string.IsNullOrEmpty(slot.overlay))
            {
                overlayAsset = GetOrCreateOverlayAsset(slot, slotAsset, slotName, slot.overlay, true);
                if (overlayAsset != null)
                {
                    overlaysForSlot.Add(overlayAsset);
                }
            } else
            {
                Debug.Log("No overlay found for slot " + slot.name);
            }

            if (slotMesh != null && slotMesh.sharedMaterials != null && slotMesh.sharedMaterials.Length > 1)
            {
                List<string> additionalOverlayNames = GetAdditionalOverlayNames(slot.overlay, slotMesh);
                foreach (string additionalOverlayName in additionalOverlayNames)
                {
                    OverlayDataAsset additionalOverlay = GetOrCreateOverlayAsset(slot, slotAsset, slotName, additionalOverlayName, false);
                    if (additionalOverlay != null && !overlaysForSlot.Contains(additionalOverlay))
                    {
                        overlaysForSlot.Add(additionalOverlay);
                    }
                }

            }

            List<string> resolvedOverlayNames = new List<string>();
            foreach (OverlayDataAsset resolvedOverlay in overlaysForSlot)
            {
                if (resolvedOverlay != null)
                {
                    resolvedOverlayNames.Add(resolvedOverlay.overlayName);
                }
            }

            Debug.Log("[UMAConverter] Slot '" + slot.name + "' resolved overlays: count=" + resolvedOverlayNames.Count + ", names=[" + string.Join(",", resolvedOverlayNames) + "].");

            if (overlayAsset == null && overlaysForSlot.Count > 0)
            {
                overlayAsset = overlaysForSlot[0];
            }

            // ----------------- Wardrobe Recipe -----------------
            // This is only needed for cloth. Every Cloth slot has a Wardrobe Recipe
            if (this.data.type == UMADataType.cloth)
            {
                string recipePath = workingDirectory + "/Wardrobe/" + slot.name + "_Recipe";
                UMAWardrobeRecipe recipe = CreateRecipe(recipePath, slotAsset, overlayAsset, addToGlobalLibrary, slot.wardrobeSlot);
                #if UMAConverterGCInventory
                if (UMAConverterSettings.Instance.CreateItems)
                {
                    UMAConverter.integrations.GameCreatorInventory.CreateItem(recipe, workingDirectory + "/Items/", slot.name);
                }
                #endif
            }
            if (slotAsset == null)
            {
                Debug.LogWarning("That should not happen.");
            }
            if(overlayAsset == null)
            {
                Debug.LogWarning("That should not happen.");
            }

            raceSlots.Add(new UMAData_RaceSlots(slotAsset, overlaysForSlot));




            return slotAsset;

        }

        private OverlayDataAsset GetOrCreateOverlayAsset(UMAData_Slot slot, SlotDataAsset slotAsset, string slotName, string overlayName, bool allowSharedPath)
        {
            if (string.IsNullOrEmpty(overlayName))
            {
                return null;
            }

            bool useSharedPath = allowSharedPath && slot.isSharedOverlay(this.data) && overlayName == slot.overlay;
            string overlayPath = useSharedPath
                ? workingDirectory + "/Overlays/" + overlayName
                : workingDirectory + "/Slots/" + slot.name + "/" + overlayName;

            OverlayDataAsset overlayAsset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath + ".asset");
            if (overlayAsset == null)
            {
                overlayAsset = CreateOverlay(overlayPath, slotAsset, slotName, overlayName);
            }
            else
            {
                UpdateOverlay(overlayAsset, overlayPath, slotAsset, slotName, overlayName);
            }

            return overlayAsset;
        }

        private List<string> GetAdditionalOverlayNames(string primaryOverlayName, SkinnedMeshRenderer slotMesh)
        {
            List<string> overlayNames = new List<string>();
            if (slotMesh == null || slotMesh.sharedMaterials == null)
            {
                return overlayNames;
            }

            foreach (Material sourceMaterial in slotMesh.sharedMaterials)
            {
                if (sourceMaterial == null || string.IsNullOrEmpty(sourceMaterial.name))
                {
                    continue;
                }

                if (string.Equals(sourceMaterial.name, primaryOverlayName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool alreadyAdded = false;
                foreach (string overlayName in overlayNames)
                {
                    if (string.Equals(overlayName, sourceMaterial.name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                {
                    overlayNames.Add(sourceMaterial.name);
                }
            }

            return overlayNames;
        }

        public OverlayDataAsset CreateOverlay(string overlayPath, SlotDataAsset slotAsset, string slotName, string overlayName = null)
        {
            Debug.Log("[UMAConverter] CreateOverlay start: overlayPath='" + overlayPath + "', slotName='" + slotName + "', overlayName='" + overlayName + "'");

            OverlayDataAsset asset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            ApplyOverlayData(asset, slotAsset, slotName, overlayName, "CreateOverlay");


            AssetDatabase.CreateAsset(asset, overlayPath +".asset");
            AssetDatabase.SaveAssets();
            return asset;

        }

        private void UpdateOverlay(OverlayDataAsset asset, string overlayPath, SlotDataAsset slotAsset, string slotName, string overlayName)
        {
            Debug.Log("[UMAConverter] UpdateOverlay start: overlayPath='" + overlayPath + "', slotName='" + slotName + "', overlayName='" + overlayName + "'");
            ApplyOverlayData(asset, slotAsset, slotName, overlayName, "UpdateOverlay");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private void ApplyOverlayData(OverlayDataAsset asset, SlotDataAsset slotAsset, string slotName, string overlayName, string source)
        {
            asset.overlayName = slotName;
            if (!string.IsNullOrEmpty(overlayName))
            {
                asset.overlayName = overlayName;
            }

            asset.material = slotAsset.material;

            string textureOverlayName = !string.IsNullOrEmpty(overlayName) ? overlayName : slotName;
            Texture[] slotMaterialTextures = GetOverlayTextureList(textureOverlayName, slotAsset.material);
            Texture[] defaultMaterialTextures = GetOverlayTextureList(textureOverlayName, UMAConverterSettings.Instance.defaultMaterial);

            bool hasSlotTextures = false;
            foreach (Texture texture in slotMaterialTextures)
            {
                if (texture != null)
                {
                    hasSlotTextures = true;
                    break;
                }
            }

            asset.textureList = hasSlotTextures ? slotMaterialTextures : defaultMaterialTextures;

            Debug.Log("[UMAConverter] " + source + " textures resolved: overlay='" + textureOverlayName + "', slotMaterial=" + slotMaterialTextures.Length + ", defaultMaterial=" + defaultMaterialTextures.Length + ", assigned=" + asset.textureList.Length + ", usedSlotTextures=" + hasSlotTextures);
        }

        /// <summary>
        /// This method takes a overlayName and will return a list of textures which are found in the overlay folder.
        /// Texture names are given by [OverlayName]_[Channel].ext
        /// The Channel is given by the umaMaterial.channels[0].materialPropertyName and will be added as suffix to the overlayName.
        /// Resulting in a texture like Body_BaseMap.* or Body_NormalMap.*.
        /// We will return the list in the order of the umaMaterial.channels
        /// </summary>
        /// <param name="overlayName"></param>
        /// <returns></returns>
        public Texture[] GetOverlayTextureList(string overlayName, UMAMaterial umaMaterial)
        {
            List<Texture> textures = new List<Texture>();
            string[] searchFolders = GetTextureSearchFolders();
            int resolvedTextureCount = 0;

            Debug.Log("[UMAConverter] GetOverlayTextureList start: overlayName='" + overlayName + "', material='" + (umaMaterial != null ? umaMaterial.name : "null") + "', channels=" + (umaMaterial != null ? umaMaterial.channels.Length : 0) + ", searchFolders=" + string.Join(",", searchFolders));

            if (umaMaterial == null)
            {
                Debug.LogWarning("[UMAConverter] GetOverlayTextureList aborted: umaMaterial is null");
                return textures.ToArray();
            }

            foreach (UMAMaterial.MaterialChannel channel in umaMaterial.channels)
            {
                string channelName = channel.materialPropertyName.Replace("_", "");
                List<string> candidates = GetTextureCandidatesForChannel(channelName);

                Debug.Log("[UMAConverter] Channel '" + channel.materialPropertyName + "' candidates: " + string.Join(",", candidates));

                Texture texture = null;
                foreach (string candidate in candidates)
                {
                    string textureName = overlayName + "_" + candidate;
                    texture = FindTextureByName(textureName, searchFolders);
                    if (texture != null)
                    {
                        Debug.Log("[UMAConverter] Matched channel '" + channel.materialPropertyName + "' with candidate '" + candidate + "' => texture '" + texture.name + "'");
                        break;
                    }
                }

                if (texture != null)
                {
                    resolvedTextureCount++;
                }
                else
                {
                    Debug.LogWarning("[UMAConverter] No texture found for channel '" + channel.materialPropertyName + "' (overlay='" + overlayName + "')");
                }

                textures.Add(texture);
            }

            Debug.Log("[UMAConverter] GetOverlayTextureList end: resolvedTextures=" + resolvedTextureCount + ", returnedSlots=" + textures.Count);
            return textures.ToArray();  
        }

        private string[] GetTextureSearchFolders()
        {
            List<string> searchFolders = new List<string>();
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                string textureFolder = workingDirectory + "/Textures";
                if (AssetDatabase.IsValidFolder(textureFolder))
                {
                    searchFolders.Add(textureFolder);
                }

                if (AssetDatabase.IsValidFolder(workingDirectory))
                {
                    searchFolders.Add(workingDirectory);
                }
            }
            return searchFolders.ToArray();
        }

        private Texture FindTextureByName(string textureName, string[] searchFolders)
        {
            string[] textureGUIDs = searchFolders.Length > 0
                ? AssetDatabase.FindAssets(textureName + " t:Texture", searchFolders)
                : AssetDatabase.FindAssets(textureName + " t:Texture");

            List<string> exactMatchPaths = new List<string>();

            foreach (string guid in textureGUIDs)
            {
                string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(texturePath), textureName, System.StringComparison.OrdinalIgnoreCase))
                {
                    exactMatchPaths.Add(texturePath);
                }
            }

            if (exactMatchPaths.Count == 0)
            {
                return null;
            }

            string selectedPath = exactMatchPaths[0];
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                string preferredPrefix = workingDirectory + "/Textures/";
                foreach (string candidatePath in exactMatchPaths)
                {
                    if (candidatePath.StartsWith(preferredPrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        selectedPath = candidatePath;
                        break;
                    }
                }
            }

            if (exactMatchPaths.Count > 1)
            {
                Debug.LogWarning("[UMAConverter] Multiple exact textures found for '" + textureName + "': [" + string.Join(",", exactMatchPaths) + "]. Selected='" + selectedPath + "'.");
            }

            Texture resolvedTexture = AssetDatabase.LoadAssetAtPath<Texture>(selectedPath);
            Texture2D resolvedTexture2D = resolvedTexture as Texture2D;
            string sizeInfo = resolvedTexture2D != null
                ? resolvedTexture2D.width + "x" + resolvedTexture2D.height
                : "unknown";
            Hash128 dependencyHash = AssetDatabase.GetAssetDependencyHash(selectedPath);

            Debug.Log("[UMAConverter] Resolved texture '" + textureName + "' => path='" + selectedPath + "', size='" + sizeInfo + "', hash='" + dependencyHash + "'.");
            return resolvedTexture;

        }

        private List<string> GetTextureCandidatesForChannel(string channelName)
        {
            List<string> aliases;
            if (!textureChannelAliases.TryGetValue(channelName, out aliases))
            {
                aliases = new List<string>();
            }

            List<string> candidates = new List<string>();
            candidates.Add(channelName);
            foreach (string alias in aliases)
            {
                if (!candidates.Contains(alias))
                {
                    candidates.Add(alias);
                }
            }
            return candidates;
        }

        private static readonly Dictionary<string, List<string>> textureChannelAliases = new Dictionary<string, List<string>>()
        {
            { "Diffuse", new List<string> { "BaseMap", "MainTex", "Albedo", "Color" } },
            { "BaseMap", new List<string> { "Diffuse", "MainTex", "Albedo", "Color" } },
            { "MainTex", new List<string> { "Diffuse", "BaseMap", "Albedo", "Color" } },
            { "Normal", new List<string> { "BumpMap", "NormalMap" } },
            { "BumpMap", new List<string> { "Normal", "NormalMap" } },
            { "metallic", new List<string> { "Metallic", "MetallicGlossMap", "metallic" } },
            { "Metallic", new List<string> { "metallic", "MetallicGlossMap" } },
            { "roughness", new List<string> { "Smoothness", "Glossiness", "roughness" } },
            { "Smoothness", new List<string> { "roughness", "Glossiness" } },
            { "EmissionMap", new List<string> { "Emission", "Emissive" } },
            { "OcclusionMap", new List<string> { "Occlusion", "AO", "AmbientOcclusion" } },
            { "ParallaxMap", new List<string> { "Height", "Displacement" } },
            { "SpecGlossMap", new List<string> { "Specular", "SpecGloss", "SpecularGloss" } },
            { "DetailAlbedoMap", new List<string> { "DetailAlbedo", "DetailColor" } },
            { "DetailNormalMap", new List<string> { "DetailNormal", "DetailBump" } },
            { "DetailMask", new List<string>() }
        };



        private UMAWardrobeRecipe CreateRecipe(string path, SlotDataAsset slotData, OverlayDataAsset overlayData, bool addToGlobalLibrary, string wardrobeSlot)
        {
            UMA.CharacterSystem.UMAWardrobeRecipe wardrobeRecipe = UMAEditorUtilities.CreateRecipe(path + ".asset", slotData, overlayData, slotData.name, addToGlobalLibrary);
            wardrobeRecipe.wardrobeSlot = wardrobeSlot;
            wardrobeRecipe.compatibleRaces = (this.data as UMAData_Cloth).compatibleRaces;

            return wardrobeRecipe;
        }


        /// <summary>
        /// This Method takes all Slots and the UMAData_Race information and generates a UMA Compatible and Ready to use Race.
        /// It have to be called after all slots are generated or it will fail.
        /// </summary>
        /// <returns>State if the generation of a Race was a Succsess or Failed</returns>
        public bool GenerateRaceAssets()
        {
            if (this.data.type != UMADataType.race) throw new System.Exception("This method can only be called for Race Creation");

            // ---------------- Race Data ----------------

            RaceData raceData = ScriptableObject.CreateInstance<RaceData>();
            raceData.raceName = (this.data as UMAData_Race).name;
            raceData.TPose = GenerateTpose();
            raceData.FixupRotations = true;

            string raceDataPath = workingDirectory + "/Race/" + (this.data as UMAData_Race).name + "_RaceData.asset";

            AssetDatabase.CreateAsset(raceData, raceDataPath);

            if (addToGlobalLibrary)
            {
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(RaceData), raceData);
            }

            // ----------------- Race Text Recipe -----------------

            UMATextRecipe asset = ScriptableObject.CreateInstance<UMATextRecipe>();
            UMAData.UMARecipe recipe = new UMAData.UMARecipe();
            recipe.ClearDna();



            int index = 0;
            foreach(UMAData_RaceSlots raceSlot in raceSlots)
            {
                SlotData slotData = new SlotData(raceSlot.slot);

                if (raceSlot.overlays != null && raceSlot.overlays.Count > 0)
                {
                    foreach (OverlayDataAsset overlayAsset in raceSlot.overlays)
                    {
                        if (overlayAsset != null)
                        {
                            slotData.AddOverlay(new OverlayData(overlayAsset));
                        }
                    }
                }
                else if (raceSlot.overlay != null)
                {
                    slotData.AddOverlay(new OverlayData(raceSlot.overlay));
                }

                recipe.SetSlot(index, slotData);
                index++;
            }
            recipe.SetRace(raceData);
            asset.Save(recipe, UMAContextBase.Instance);
            asset.DisplayValue = (this.data as UMAData_Race).name + "_TextRecipe";

            string textRecipePath = workingDirectory + "/Race/" + (this.data as UMAData_Race).name + "_TextRecipe.asset";

            // Write the asset to disk
            AssetDatabase.CreateAsset(asset, textRecipePath);
            AssetDatabase.SaveAssets();
            if (addToGlobalLibrary)
            {
                // Add it to the global libary
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMA.CharacterSystem.UMAWardrobeRecipe), asset);
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMATextRecipe), asset);

                EditorUtility.SetDirty(UMAAssetIndexer.Instance);

            }


            raceData.baseRaceRecipe = asset;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            EditorUtility.SetDirty(raceData);
            AssetDatabase.SaveAssetIfDirty(raceData);
            AssetDatabase.SaveAssets();

#if UMAConverterGCInventory
            integrations.GameCreatorInventory.CreateEquipmentAsset(null, workingDirectory + "/Race/"+(this.data as UMAData_Race).name+"_"); // TODO: Add the ability to customize the Wardrobe Slots, needs update of the Blender Exporter
#endif



            AssetDatabase.Refresh();
            return true;

        }

        /// <summary>
        /// Generates a TPose for the Race and saves it to its folder.
        /// It should be called only for Race Creation.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="System.Exception"></exception>
        public UmaTPose GenerateTpose()
        {
            if (this.data.type != UMADataType.race) throw new System.Exception("This method can only be called for Race Creation");
            string TPosePath = workingDirectory + "/TPose/" + (this.data as UMAData_Race).name + "_TPose.asset";

            ModelImporter modelImporter = AssetImporter.GetAtPath(meshPath) as ModelImporter;
            if(modelImporter != null)
            {
                var asset = UmaTPose.CreateInstance<UMA.UmaTPose>();
                asset.ReadFromHumanDescription(modelImporter.humanDescription);
                AssetDatabase.CreateAsset(asset, TPosePath);
                return asset;
            } else
            {
                throw new System.Exception("Failed to load ModelImporter for " + meshPath);
            }

        }

        /// <summary>
        /// Called by the AssetPreprocessor to convert the FBX and UMAData to UMA Assets
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public void convert()
        {
            if (data == null) throw new System.Exception("UMAData not found for " + meshPath);
            CreateFolderStructure();
            foreach (UMAData_Slot slot in data.slots)
            {

                GenerateSlotAsset(slot);
            }
            if(data.type == UMADataType.race)
            {
                GenerateRaceAssets();
            }
            AssetDatabase.SaveAssets();
            if(UMAConverterSettings.Instance.removeMeshAfterCreating)
            {
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.DeleteAsset(jsonPath);
                AssetDatabase.Refresh();
            }

        }
    }


    [System.Serializable]
    public enum UMADataType
    {
        cloth = 0,
        race = 1
    }


    public interface IUMAData
    {
        UMADataType type { get; set; }
        List<string> meshes { get; set; }
        List<string> overlays { get; set; }
        List<UMAData_Slot> slots { get; set; }
    }


    [System.Serializable]
    public class UMAData_Race : IUMAData
    {
        public UMADataType type { get; set; }
        public string name { get; set; }
        public float hipHeight { get; set; }
        public List<string> meshes { get; set; }
        public List<string> overlays { get; set; }
        public List<UMAData_Slot> slots { get; set; }
    }
    [System.Serializable]
    public class UMAData_Cloth : IUMAData
    {
        public UMADataType type { get; set; }
        public List<string> compatibleRaces { get; set; }
        public List<string> meshes { get; set; }
        public List<string> overlays { get; set; }
        public List<UMAData_Slot> slots { get; set; }
    }
    [System.Serializable]
    public class UMAData_Slot
    {
        public string name;
        public string mesh;
        public string overlay;
        public string wardrobeSlot = "";


        /// <summary>
        /// Checks if the slot is sharing its overlay with another slot.
        /// </summary>
        /// <param name="data">The data to itterate over the others...</param>
        /// <returns></returns>
        public bool isSharedOverlay(IUMAData data)
        {
            foreach (UMAData_Slot slot in data.slots)
            {
                if (slot.name != this.name && slot.overlay == this.overlay) return true;
            }
            return false;
        }
    }



    public class UMAData_RaceSlots
    {
        public SlotDataAsset slot;
        public OverlayDataAsset overlay;
        public List<OverlayDataAsset> overlays;

        public UMAData_RaceSlots(SlotDataAsset slot, OverlayDataAsset overlay)
        {
            this.slot = slot;
            this.overlay = overlay;
            this.overlays = new List<OverlayDataAsset>();
            if (overlay != null)
            {
                this.overlays.Add(overlay);
            }
        }

        public UMAData_RaceSlots(SlotDataAsset slot, List<OverlayDataAsset> overlays)
        {
            this.slot = slot;
            this.overlays = overlays ?? new List<OverlayDataAsset>();
            this.overlay = this.overlays.Count > 0 ? this.overlays[0] : null;
        }
    }
}