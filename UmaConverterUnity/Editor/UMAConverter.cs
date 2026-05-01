using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using UMA;
using UMA.Editors;
using UMA.CharacterSystem;
using UMA.PoseTools;
using UMAConverter;
using UMAConverter.Editor.Transparency;

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

        private Dictionary<string, bool> overlayTransparencyMap = new Dictionary<string, bool>();
        private Dictionary<string, OverlayDataAsset> sharedOverlayCache = new Dictionary<string, OverlayDataAsset>(System.StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, OverlayDataAsset> sharedOverlayByTextureSignature = new Dictionary<string, OverlayDataAsset>(System.StringComparer.Ordinal);
        private Dictionary<string, string> textureFileHashCache = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

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
            if (this.model == null) throw new System.Exception("Model not found at " + MeshPath);

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
                if (!Directory.Exists(folderPath + "/Expressions")) Directory.CreateDirectory(folderPath + "/Expressions");
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
        /// For multi-material meshes, it creates separate slot+overlay+wardrobe combos for each material.
        /// </summary>
        /// <param name="slot"></param>
        /// <returns>Primary slot asset, or first generated asset if multi-material</returns>
        public SlotDataAsset GenerateSlotAsset(UMAData_Slot slot)
        {

            Debug.Log("[UMAConverter] GenerateSlotAsset start: slot='" + slot.name + "', meshPath='" + slot.mesh + "', primaryOverlay='" + slot.overlay + "', wardrobeSlot='" + slot.wardrobeSlot + "'.");

            // Get the mesh renderer
            Transform slotTransform = model != null ? model.transform.Find(slot.mesh) : null;
            SkinnedMeshRenderer slotMesh = slotTransform != null ? slotTransform.GetComponent<SkinnedMeshRenderer>() : null;

            if (slotTransform == null)
            {
                Debug.LogWarning("[UMAConverter] Slot '" + slot.name + "' mesh transform was not found for path '" + slot.mesh + "'.");
                return null;
            }
            else if (slotMesh == null)
            {
                Debug.LogWarning("[UMAConverter] Slot '" + slot.name + "' transform '" + slotTransform.name + "' has no SkinnedMeshRenderer.");
                return null;
            }
            else
            {
                string sharedMeshName = slotMesh.sharedMesh != null ? slotMesh.sharedMesh.name : "<null>";
                Debug.Log("[UMAConverter] Slot '" + slot.name + "' mesh resolved: renderer='" + slotMesh.name + "', sharedMesh='" + sharedMeshName + "'.");
            }

            // Log mesh and material information
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

            // Check if this is a multi-material mesh
            bool isMultiMaterial = slotMesh != null && slotMesh.sharedMaterials != null && slotMesh.sharedMaterials.Length > 1;

            if (isMultiMaterial)
            {
                Debug.Log("[UMAConverter] MULTI-MATERIAL SLOT DETECTED: slot='" + slot.name + "' has " + slotMesh.sharedMaterials.Length + " materials. Creating separate slot+overlay+wardrobe for each material.");
                return GenerateMultiMaterialSlots(slot, slotMesh, sourceMaterialNames);
            }
            else
            {
                return GenerateSingleMaterialSlot(slot, slotMesh);
            }
        }

        /// <summary>
        /// Generates slots for a single-material mesh (original behavior)
        /// </summary>
        private SlotDataAsset GenerateSingleMaterialSlot(UMAData_Slot slot, SkinnedMeshRenderer slotMesh)
        {
            string slotFolder = workingDirectory + "/Slots";
            string assetFolder = "";
            string assetName = slot.name;
            string slotName = slot.name;
            bool nameByMaterial = false;
            UMAMaterial material = UMAConverterSettings.Instance.defaultMaterial;
            SkinnedMeshRenderer seamsMesh = null;
            List<string> keepBoneNames = new List<string>();
            string rootBone = "Global";
            bool binarySerialization = false;
            bool calcTangents = true;
            string stripBones = "";

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

            SlotDataAsset slotAsset = UMASlotProcessingUtil.CreateSlotData(slotBuilderParameters);

            Debug.Log("[UMAConverter] Slot asset created: slot='" + slot.name + "', slotAsset='" + (slotAsset != null ? slotAsset.name : "<null>") + "', path='" + GetAssetPathSafe(slotAsset) + "', linkedMesh='" + (slotMesh != null ? slotMesh.name : "<null>") + "'.");

            slotAsset.tags = new string[0];
            UMAUpdateProcessor.UpdateSlot(slotAsset);

            if (addToGlobalLibrary)
            {
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), slotAsset);
            }

            // Create overlay
            OverlayDataAsset overlayAsset = null;
            List<OverlayDataAsset> overlaysForSlot = new List<OverlayDataAsset>();

            if (!string.IsNullOrEmpty(slot.overlay))
            {
                overlayAsset = GetOrCreateOverlayAsset(slot, slotAsset, slotName, slot.overlay, true);
                if (overlayAsset != null)
                {
                    overlaysForSlot.Add(overlayAsset);
                    Debug.Log("[UMAConverter] Primary overlay linked: slot='" + slot.name + "', slotAsset='" + slotAsset.name + "', overlay='" + overlayAsset.overlayName + "', path='" + GetAssetPathSafe(overlayAsset) + "'.");
                }
            }
            else
            {
                Debug.Log("No overlay found for slot " + slot.name);
            }

            if (overlayAsset == null && overlaysForSlot.Count > 0)
            {
                overlayAsset = overlaysForSlot[0];
            }

            // Create wardrobe recipe for cloth
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

            raceSlots.Add(new UMAData_RaceSlots(slotAsset, overlaysForSlot));

            List<string> linkedOverlayPairs = new List<string>();
            foreach (OverlayDataAsset linkedOverlay in overlaysForSlot)
            {
                if (linkedOverlay != null)
                {
                    linkedOverlayPairs.Add(linkedOverlay.overlayName + "@" + GetAssetPathSafe(linkedOverlay));
                }
            }

            Debug.Log("[UMAConverter] GenerateSlotAsset end (single-material): slot='" + slot.name + "', mesh='" + (slotMesh != null ? slotMesh.name : "<null>") + "', slotAsset='" + (slotAsset != null ? slotAsset.name : "<null>") + "@" + GetAssetPathSafe(slotAsset) + "', overlaysLinked=" + linkedOverlayPairs.Count + " => [" + string.Join(",", linkedOverlayPairs) + "].");

            return slotAsset;
        }

        /// <summary>
        /// For multi-material meshes, creates separate slot+overlay for each material, but combines them into a single layered wardrobe recipe.
        /// Each material becomes its own independent slot that can be toggled separately in the same wardrobe item.
        /// </summary>
        private SlotDataAsset GenerateMultiMaterialSlots(UMAData_Slot slot, SkinnedMeshRenderer slotMesh, List<string> sourceMaterialNames)
        {
            SlotDataAsset primarySlotAsset = null;
            List<int> materialIndices = new List<int>();
            List<SlotDataAsset> layeredSlots = new List<SlotDataAsset>();
            List<OverlayDataAsset> layeredOverlays = new List<OverlayDataAsset>();

            if (slotMesh == null || slotMesh.sharedMaterials == null)
            {
                Debug.LogWarning("[UMAConverter] Multi-material slot generation aborted: no shared materials for slot '" + slot.name + "'.");
                return null;
            }

            for (int materialIndex = 0; materialIndex < slotMesh.sharedMaterials.Length; materialIndex++)
            {
                Material sourceMaterial = slotMesh.sharedMaterials[materialIndex];
                if (sourceMaterial != null && !string.IsNullOrEmpty(sourceMaterial.name))
                {
                    materialIndices.Add(materialIndex);
                }
            }

            if (materialIndices.Count == 0)
            {
                Debug.LogWarning("[UMAConverter] Multi-material slot generation found no valid material names for slot '" + slot.name + "'. Falling back to single-material generation.");
                return GenerateSingleMaterialSlot(slot, slotMesh);
            }

            // Keep the json primary overlay first when present.
            if (!string.IsNullOrEmpty(slot.overlay))
            {
                int primaryMaterialIndex = -1;
                for (int i = 0; i < materialIndices.Count; i++)
                {
                    int candidateIndex = materialIndices[i];
                    Material candidateMaterial = slotMesh.sharedMaterials[candidateIndex];
                    if (candidateMaterial != null && string.Equals(candidateMaterial.name, slot.overlay, System.StringComparison.OrdinalIgnoreCase))
                    {
                        primaryMaterialIndex = i;
                        break;
                    }
                }

                if (primaryMaterialIndex > 0)
                {
                    int matchedIndex = materialIndices[primaryMaterialIndex];
                    materialIndices.RemoveAt(primaryMaterialIndex);
                    materialIndices.Insert(0, matchedIndex);
                }
            }

            Dictionary<string, int> slotNameCounts = new Dictionary<string, int>();

            // Create separate slot+overlay for each material (collected for layered wardrobe)
            for (int i = 0; i < materialIndices.Count; i++)
            {
                int materialIndex = materialIndices[i];
                Material sourceMaterial = slotMesh.sharedMaterials[materialIndex];
                string materialName = sourceMaterial != null ? sourceMaterial.name : ("SubMesh" + materialIndex);
                string uniqueSlotNameBase = slot.name + "_" + materialName;
                string uniqueSlotName = uniqueSlotNameBase;
                if (slotNameCounts.ContainsKey(uniqueSlotNameBase))
                {
                    slotNameCounts[uniqueSlotNameBase]++;
                    uniqueSlotName = uniqueSlotNameBase + "_" + slotNameCounts[uniqueSlotNameBase];
                }
                else
                {
                    slotNameCounts[uniqueSlotNameBase] = 1;
                }

                string slotFolder = workingDirectory + "/Slots";
                string assetFolder = "";
                string assetName = uniqueSlotName;
                bool nameByMaterial = false;
                UMAMaterial material = UMAConverterSettings.Instance.defaultMaterial;
                SkinnedMeshRenderer seamsMesh = null;
                List<string> keepBoneNames = new List<string>();
                string rootBone = "Global";
                bool binarySerialization = false;
                bool calcTangents = true;
                string stripBones = "";

                Debug.Log("[UMAConverter] Creating sub-slot for material: originalSlot='" + slot.name + "', material='" + materialName + "', subMeshIndex=" + materialIndex + ", uniqueSlotName='" + uniqueSlotName + "'.");

                GameObject isolatedRendererObject = null;
                GameObject isolatedRendererRoot = null;
                Mesh isolatedMesh = null;
                SkinnedMeshRenderer isolatedRenderer = CreateSingleSubmeshRenderer(slotMesh, materialIndex, uniqueSlotName, out isolatedRendererRoot, out isolatedRendererObject, out isolatedMesh);
                if (isolatedRenderer == null)
                {
                    Debug.LogWarning("[UMAConverter] Failed to create isolated renderer for slot '" + slot.name + "', material='" + materialName + "', subMeshIndex=" + materialIndex + ".");
                    continue;
                }

                SlotDataAsset subSlotAsset = null;
                try
                {
                    SlotBuilderParameters slotBuilderParameters = new SlotBuilderParameters();
                    slotBuilderParameters.slotFolder = slotFolder;
                    slotBuilderParameters.assetFolder = assetFolder;
                    slotBuilderParameters.assetName = assetName;
                    slotBuilderParameters.slotName = uniqueSlotName;
                    slotBuilderParameters.nameByMaterial = nameByMaterial;
                    slotBuilderParameters.slotMesh = isolatedRenderer;
                    slotBuilderParameters.material = material;
                    slotBuilderParameters.seamsMesh = seamsMesh;
                    slotBuilderParameters.keepList = keepBoneNames;
                    slotBuilderParameters.rootBone = rootBone;
                    slotBuilderParameters.binarySerialization = binarySerialization;
                    slotBuilderParameters.calculateTangents = calcTangents;
                    slotBuilderParameters.stripBones = stripBones;

                    subSlotAsset = UMASlotProcessingUtil.CreateSlotData(slotBuilderParameters);
                }
                finally
                {
                    if (isolatedRendererRoot != null)
                    {
                        UnityEngine.Object.DestroyImmediate(isolatedRendererRoot);
                    }

                    else if (isolatedRendererObject != null)
                    {
                        UnityEngine.Object.DestroyImmediate(isolatedRendererObject);
                    }

                    if (isolatedMesh != null)
                    {
                        UnityEngine.Object.DestroyImmediate(isolatedMesh);
                    }
                }

                Debug.Log("[UMAConverter] Sub-slot asset created: originalSlot='" + slot.name + "', material='" + materialName + "', slotAsset='" + (subSlotAsset != null ? subSlotAsset.name : "<null>") + "', path='" + GetAssetPathSafe(subSlotAsset) + "'.");

                if (subSlotAsset != null)
                {
                    subSlotAsset.tags = new string[0];
                    UMAUpdateProcessor.UpdateSlot(subSlotAsset);

                    if (addToGlobalLibrary)
                    {
                        UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), subSlotAsset);
                    }

                    layeredSlots.Add(subSlotAsset);

                    // Create overlay for this material
                    OverlayDataAsset overlayAsset = null;
                    if (!string.IsNullOrEmpty(materialName))
                    {
                        overlayAsset = GetOrCreateOverlayAsset(slot, subSlotAsset, uniqueSlotName, materialName, true);
                        if (overlayAsset != null)
                        {
                            Debug.Log("[UMAConverter] Sub-overlay created: originalSlot='" + slot.name + "', material='" + materialName + "', overlay='" + overlayAsset.overlayName + "', path='" + GetAssetPathSafe(overlayAsset) + "'.");
                        }
                    }

                    layeredOverlays.Add(overlayAsset);

                    // Track slots for race (if applicable)
                    List<OverlayDataAsset> subSlotOverlays = new List<OverlayDataAsset>();
                    if (overlayAsset != null)
                    {
                        subSlotOverlays.Add(overlayAsset);
                    }
                    raceSlots.Add(new UMAData_RaceSlots(subSlotAsset, subSlotOverlays));

                    // Keep reference to primary slot asset
                    if (primarySlotAsset == null)
                    {
                        primarySlotAsset = subSlotAsset;
                    }
                }
            }

            // Create single layered wardrobe recipe for cloth
            if (this.data.type == UMADataType.cloth && layeredSlots.Count > 0)
            {
                string recipePath = workingDirectory + "/Wardrobe/" + slot.name + "_Recipe";
                UMAWardrobeRecipe recipe = CreateLayeredRecipe(recipePath, layeredSlots, layeredOverlays, addToGlobalLibrary, slot.wardrobeSlot);
                Debug.Log("[UMAConverter] Layered wardrobe recipe created: originalSlot='" + slot.name + "', layeredSlots=" + layeredSlots.Count + ", layeredOverlays=" + layeredOverlays.Count + ", recipePath='" + recipePath + "'");

#if UMAConverterGCInventory
                if (UMAConverterSettings.Instance.CreateItems)
                {
                    UMAConverter.integrations.GameCreatorInventory.CreateItem(recipe, workingDirectory + "/Items/", slot.name);
                }
#endif
            }

            Debug.Log("[UMAConverter] GenerateSlotAsset end (multi-material): slot='" + slot.name + "', totalMaterialSlots=" + materialIndices.Count + ", created " + layeredSlots.Count + " isolated submesh slots+overlays in single wardrobe recipe.");

            return primarySlotAsset;
        }

        private SkinnedMeshRenderer CreateSingleSubmeshRenderer(SkinnedMeshRenderer sourceRenderer, int subMeshIndex, string slotName, out GameObject tempRootObject, out GameObject tempObject, out Mesh tempMesh)
        {
            tempRootObject = null;
            tempObject = null;
            tempMesh = null;

            if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
            {
                return null;
            }

            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (subMeshIndex < 0 || subMeshIndex >= sourceMesh.subMeshCount)
            {
                Debug.LogWarning("[UMAConverter] Invalid subMeshIndex=" + subMeshIndex + " for mesh '" + sourceMesh.name + "' with subMeshCount=" + sourceMesh.subMeshCount + ".");
                return null;
            }

            int[] sourceTriangles = sourceMesh.GetTriangles(subMeshIndex);
            if (sourceTriangles == null || sourceTriangles.Length == 0)
            {
                Debug.LogWarning("[UMAConverter] Submesh " + subMeshIndex + " has no triangles for mesh '" + sourceMesh.name + "'.");
                return null;
            }

            Dictionary<int, int> vertexRemap = new Dictionary<int, int>();
            List<int> uniqueSourceVertexIndices = new List<int>();
            int[] remappedTriangles = new int[sourceTriangles.Length];

            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex++)
            {
                int sourceVertexIndex = sourceTriangles[triangleIndex];
                int remappedVertexIndex;
                if (!vertexRemap.TryGetValue(sourceVertexIndex, out remappedVertexIndex))
                {
                    remappedVertexIndex = uniqueSourceVertexIndices.Count;
                    vertexRemap[sourceVertexIndex] = remappedVertexIndex;
                    uniqueSourceVertexIndices.Add(sourceVertexIndex);
                }

                remappedTriangles[triangleIndex] = remappedVertexIndex;
            }

            tempMesh = new Mesh();
            tempMesh.name = sourceMesh.name + "_" + slotName + "_SubMesh";
            tempMesh.vertices = RemapVector3Array(sourceMesh.vertices, uniqueSourceVertexIndices);
            tempMesh.bindposes = sourceMesh.bindposes;

            BoneWeight[] sourceBoneWeights = sourceMesh.boneWeights;
            if (sourceBoneWeights != null && sourceBoneWeights.Length == sourceMesh.vertexCount)
            {
                BoneWeight[] remappedBoneWeights = new BoneWeight[uniqueSourceVertexIndices.Count];
                for (int remappedVertexIndex = 0; remappedVertexIndex < uniqueSourceVertexIndices.Count; remappedVertexIndex++)
                {
                    remappedBoneWeights[remappedVertexIndex] = sourceBoneWeights[uniqueSourceVertexIndices[remappedVertexIndex]];
                }

                tempMesh.boneWeights = remappedBoneWeights;
            }

            if (sourceMesh.normals != null && sourceMesh.normals.Length == sourceMesh.vertexCount)
            {
                tempMesh.normals = RemapVector3Array(sourceMesh.normals, uniqueSourceVertexIndices);
            }

            if (sourceMesh.tangents != null && sourceMesh.tangents.Length == sourceMesh.vertexCount)
            {
                tempMesh.tangents = RemapVector4Array(sourceMesh.tangents, uniqueSourceVertexIndices);
            }

            if (sourceMesh.colors != null && sourceMesh.colors.Length == sourceMesh.vertexCount)
            {
                tempMesh.colors = RemapColorArray(sourceMesh.colors, uniqueSourceVertexIndices);
            }

            if (sourceMesh.uv != null && sourceMesh.uv.Length == sourceMesh.vertexCount)
            {
                tempMesh.uv = RemapVector2Array(sourceMesh.uv, uniqueSourceVertexIndices);
            }

            if (sourceMesh.uv2 != null && sourceMesh.uv2.Length == sourceMesh.vertexCount)
            {
                tempMesh.uv2 = RemapVector2Array(sourceMesh.uv2, uniqueSourceVertexIndices);
            }

            if (sourceMesh.uv3 != null && sourceMesh.uv3.Length == sourceMesh.vertexCount)
            {
                tempMesh.uv3 = RemapVector2Array(sourceMesh.uv3, uniqueSourceVertexIndices);
            }

            if (sourceMesh.uv4 != null && sourceMesh.uv4.Length == sourceMesh.vertexCount)
            {
                tempMesh.uv4 = RemapVector2Array(sourceMesh.uv4, uniqueSourceVertexIndices);
            }

            tempMesh.subMeshCount = 1;
            tempMesh.SetTriangles(remappedTriangles, 0);
            tempMesh.RecalculateBounds();
            if (tempMesh.normals == null || tempMesh.normals.Length == 0)
            {
                tempMesh.RecalculateNormals();
            }

            Debug.Log("[UMAConverter] Isolated submesh mesh prepared: sourceMesh='" + sourceMesh.name + "', subMeshIndex=" + subMeshIndex + ", sourceVertexCount=" + sourceMesh.vertexCount + ", isolatedVertexCount=" + tempMesh.vertexCount + ", sourceTriangles=" + (sourceTriangles.Length / 3) + ", isolatedTriangles=" + (remappedTriangles.Length / 3) + ".");

            Transform sourceParent = sourceRenderer.transform.parent;
            if (sourceParent != null)
            {
                tempRootObject = UnityEngine.Object.Instantiate(sourceParent.gameObject);
                tempRootObject.name = "__UMAConverterRoot_" + slotName + "_SubMesh_" + subMeshIndex;

                SkinnedMeshRenderer[] candidateRenderers = tempRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (SkinnedMeshRenderer candidateRenderer in candidateRenderers)
                {
                    if (candidateRenderer.name == sourceRenderer.name)
                    {
                        tempObject = candidateRenderer.gameObject;
                        break;
                    }
                }
            }

            if (tempObject == null)
            {
                tempRootObject = new GameObject("__UMAConverterRoot_" + slotName + "_SubMesh_" + subMeshIndex);
                tempObject = new GameObject(sourceRenderer.name);
                tempObject.transform.SetParent(tempRootObject.transform, false);
                tempObject.AddComponent<SkinnedMeshRenderer>();
            }

            tempObject.name = sourceRenderer.name;
            SkinnedMeshRenderer isolatedRenderer = tempObject.GetComponent<SkinnedMeshRenderer>();
            if (isolatedRenderer == null)
            {
                isolatedRenderer = tempObject.AddComponent<SkinnedMeshRenderer>();
            }

            isolatedRenderer.sharedMesh = tempMesh;

            if (isolatedRenderer.bones == null || isolatedRenderer.bones.Length == 0)
            {
                isolatedRenderer.bones = sourceRenderer.bones;
            }

            if (isolatedRenderer.rootBone == null)
            {
                isolatedRenderer.rootBone = sourceRenderer.rootBone;
            }

            Material isolatedMaterial = null;
            if (sourceRenderer.sharedMaterials != null && subMeshIndex < sourceRenderer.sharedMaterials.Length)
            {
                isolatedMaterial = sourceRenderer.sharedMaterials[subMeshIndex];
            }

            isolatedRenderer.sharedMaterials = isolatedMaterial != null ? new Material[] { isolatedMaterial } : new Material[0];
            return isolatedRenderer;
        }

        private Vector3[] RemapVector3Array(Vector3[] sourceValues, List<int> sourceIndices)
        {
            Vector3[] remappedValues = new Vector3[sourceIndices.Count];
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                remappedValues[i] = sourceValues[sourceIndices[i]];
            }

            return remappedValues;
        }

        private Vector4[] RemapVector4Array(Vector4[] sourceValues, List<int> sourceIndices)
        {
            Vector4[] remappedValues = new Vector4[sourceIndices.Count];
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                remappedValues[i] = sourceValues[sourceIndices[i]];
            }

            return remappedValues;
        }

        private Vector2[] RemapVector2Array(Vector2[] sourceValues, List<int> sourceIndices)
        {
            Vector2[] remappedValues = new Vector2[sourceIndices.Count];
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                remappedValues[i] = sourceValues[sourceIndices[i]];
            }

            return remappedValues;
        }

        private Color[] RemapColorArray(Color[] sourceValues, List<int> sourceIndices)
        {
            Color[] remappedValues = new Color[sourceIndices.Count];
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                remappedValues[i] = sourceValues[sourceIndices[i]];
            }

            return remappedValues;
        }

        /// <summary>
        /// Creates a wardrobe recipe that layers multiple slots together (for multi-material meshes).
        /// </summary>
        private UMAWardrobeRecipe CreateLayeredRecipe(string path, List<SlotDataAsset> slotDataAssets, List<OverlayDataAsset> overlayDataAssets, bool addToGlobalLibrary, string wardrobeSlot)
        {
            // Create recipe with first slot/overlay pair as base
            UMA.CharacterSystem.UMAWardrobeRecipe wardrobeRecipe = null;
            if (slotDataAssets.Count > 0)
            {
                OverlayDataAsset firstOverlay = overlayDataAssets.Count > 0 ? overlayDataAssets[0] : null;
                wardrobeRecipe = UMAEditorUtilities.CreateRecipe(path + ".asset", slotDataAssets[0], firstOverlay, slotDataAssets[0].name, addToGlobalLibrary);
                wardrobeRecipe.wardrobeSlot = wardrobeSlot;
                wardrobeRecipe.compatibleRaces = (this.data as UMAData_Cloth).compatibleRaces;

                // Rebuild the full layered recipe and save it through UMA's Save API.
                UMAData.UMARecipe layeredRecipe = new UMAData.UMARecipe();
                layeredRecipe.ClearDna();

                for (int i = 0; i < slotDataAssets.Count; i++)
                {
                    SlotData slotData = new SlotData(slotDataAssets[i]);
                    if (i < overlayDataAssets.Count && overlayDataAssets[i] != null)
                    {
                        slotData.AddOverlay(new OverlayData(overlayDataAssets[i]));
                    }
                    layeredRecipe.SetSlot(i, slotData);
                }

                System.Reflection.MethodInfo saveMethod = wardrobeRecipe.GetType().GetMethod(
                    "Save",
                    new System.Type[] { typeof(UMAData.UMARecipe), typeof(UMAContextBase) }
                );
                if (saveMethod != null)
                {
                    saveMethod.Invoke(wardrobeRecipe, new object[] { layeredRecipe, UMAContextBase.Instance });
                }
                else
                {
                    Debug.LogWarning("[UMAConverter] Could not find Save(UMARecipe, UMAContextBase) on UMAWardrobeRecipe. Layered slots may not be persisted.");
                }

                EditorUtility.SetDirty(wardrobeRecipe);
                AssetDatabase.SaveAssetIfDirty(wardrobeRecipe);
                AssetDatabase.SaveAssets();

                if (addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMA.CharacterSystem.UMAWardrobeRecipe), wardrobeRecipe);
                }
            }

            return wardrobeRecipe;
        }

        private OverlayDataAsset GetOrCreateOverlayAsset(UMAData_Slot slot, SlotDataAsset slotAsset, string slotName, string overlayName, bool allowSharedPath)
        {
            if (string.IsNullOrEmpty(overlayName))
            {
                return null;
            }

            string textureSignature = string.Empty;
            if (allowSharedPath)
            {
                textureSignature = BuildOverlayTextureSignature(overlayName, slotAsset != null ? slotAsset.material : null);
                if (!string.IsNullOrEmpty(textureSignature))
                {
                    OverlayDataAsset cachedByTexture;
                    if (sharedOverlayByTextureSignature.TryGetValue(textureSignature, out cachedByTexture) && cachedByTexture != null)
                    {
                        Debug.Log("[UMAConverter] Overlay reused by texture signature: slot='" + slotName + "', slotAsset='" + (slotAsset != null ? slotAsset.name : "<null>") + "', overlay='" + overlayName + "', cachedOverlay='" + cachedByTexture.overlayName + "', path='" + GetAssetPathSafe(cachedByTexture) + "'.");
                        return cachedByTexture;
                    }

                    Debug.Log("[UMAConverter] Overlay signature cache miss: slot='" + slotName + "', overlay='" + overlayName + "', signaturePrefix='" + (textureSignature.Length > 24 ? textureSignature.Substring(0, 24) : textureSignature) + "'.");
                }
            }

            OverlayDataAsset cachedSharedOverlay;
            if (allowSharedPath && sharedOverlayCache.TryGetValue(overlayName, out cachedSharedOverlay) && cachedSharedOverlay != null)
            {
                string cachedOverlayPath = GetAssetPathSafe(cachedSharedOverlay);
                Debug.Log("[UMAConverter] Overlay reused from name cache: slot='" + slotName + "', slotAsset='" + (slotAsset != null ? slotAsset.name : "<null>") + "', overlay='" + overlayName + "', path='" + cachedOverlayPath + "'.");
                return cachedSharedOverlay;
            }

            bool useSharedPath = allowSharedPath && slot.isSharedOverlay(this.data) && overlayName == slot.overlay;
            string overlayPath = useSharedPath
                ? workingDirectory + "/Overlays/" + overlayName
                : workingDirectory + "/Slots/" + slotName + "/" + overlayName;

            OverlayDataAsset overlayAsset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath + ".asset");
            bool overlayAlreadyExists = overlayAsset != null;
            if (overlayAsset == null)
            {
                overlayAsset = CreateOverlay(overlayPath, slotAsset, slotName, overlayName);
            }
            else
            {
                UpdateOverlay(overlayAsset, overlayPath, slotAsset, slotName, overlayName);
            }

            Debug.Log("[UMAConverter] Overlay " + (overlayAlreadyExists ? "updated" : "created") + ": slot='" + slotName + "', slotAsset='" + (slotAsset != null ? slotAsset.name : "<null>") + "', overlay='" + overlayName + "', sharedPath=" + useSharedPath + ", path='" + overlayPath + ".asset'.");

            if (allowSharedPath && overlayAsset != null)
            {
                sharedOverlayCache[overlayName] = overlayAsset;
                if (!string.IsNullOrEmpty(textureSignature))
                {
                    sharedOverlayByTextureSignature[textureSignature] = overlayAsset;
                }
            }

            return overlayAsset;
        }

        private string BuildOverlayTextureSignature(string overlayName, UMAMaterial slotMaterial)
        {
            UMAMaterial defaultMaterial = UMAConverterSettings.Instance.defaultMaterial;
            Texture[] slotMaterialTextures = GetOverlayTextureList(overlayName, slotMaterial);
            Texture[] defaultMaterialTextures = System.Object.ReferenceEquals(slotMaterial, defaultMaterial)
                ? slotMaterialTextures
                : GetOverlayTextureList(overlayName, defaultMaterial);

            bool hasSlotTextures = false;
            foreach (Texture texture in slotMaterialTextures)
            {
                if (texture != null)
                {
                    hasSlotTextures = true;
                    break;
                }
            }

            Texture[] resolvedTextures = hasSlotTextures ? slotMaterialTextures : defaultMaterialTextures;
            return BuildTextureSignature(resolvedTextures);
        }

        private string BuildTextureSignature(Texture[] textures)
        {
            if (textures == null || textures.Length == 0)
            {
                return string.Empty;
            }

            bool hasAnyTexture = false;
            List<string> signatureParts = new List<string>();
            for (int i = 0; i < textures.Length; i++)
            {
                Texture texture = textures[i];
                if (texture == null)
                {
                    signatureParts.Add("null");
                    continue;
                }

                hasAnyTexture = true;
                string texturePath = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(texturePath))
                {
                    signatureParts.Add("memory:" + texture.name);
                    continue;
                }

                signatureParts.Add(GetTextureContentHash(texturePath));
            }

            return hasAnyTexture ? string.Join("|", signatureParts) : string.Empty;
        }

        private string GetTextureContentHash(string textureAssetPath)
        {
            string cachedHash;
            if (textureFileHashCache.TryGetValue(textureAssetPath, out cachedHash))
            {
                return cachedHash;
            }

            string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), textureAssetPath);
            if (!File.Exists(absolutePath))
            {
                Hash128 fallbackHash = AssetDatabase.GetAssetDependencyHash(textureAssetPath);
                string fallbackHashText = "asset:" + fallbackHash.ToString();
                textureFileHashCache[textureAssetPath] = fallbackHashText;
                return fallbackHashText;
            }

            using (FileStream stream = File.OpenRead(absolutePath))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                string hashText = System.BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
                textureFileHashCache[textureAssetPath] = hashText;
                return hashText;
            }
        }

        private string GetAssetPathSafe(Object asset)
        {
            if (asset == null)
            {
                return "<null>";
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath) ? "<no-asset-path>" : assetPath;
        }

        public OverlayDataAsset CreateOverlay(string overlayPath, SlotDataAsset slotAsset, string slotName, string overlayName = null)
        {
            Debug.Log("[UMAConverter] CreateOverlay start: overlayPath='" + overlayPath + "', slotName='" + slotName + "', overlayName='" + overlayName + "'");

            OverlayDataAsset asset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            ApplyOverlayData(asset, slotAsset, slotName, overlayName, "CreateOverlay");


            AssetDatabase.CreateAsset(asset, overlayPath + ".asset");
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

            string textureOverlayName = !string.IsNullOrEmpty(overlayName) ? overlayName : slotName;

            // Get textures first to populate transparency map
            UMAMaterial slotMaterial = slotAsset != null ? slotAsset.material : null;
            UMAMaterial defaultMaterial = UMAConverterSettings.Instance.defaultMaterial;
            Texture[] slotMaterialTextures = GetOverlayTextureList(textureOverlayName, slotMaterial);
            Texture[] defaultMaterialTextures = System.Object.ReferenceEquals(slotMaterial, defaultMaterial)
                ? slotMaterialTextures
                : GetOverlayTextureList(textureOverlayName, defaultMaterial);

            // Determine which material to use based on transparency policy
            UMAMaterial materialToUse = UMAConverterSettings.Instance.defaultMaterial;
            if (TransparencyOverlayPolicy.ShouldUseTransparentRendering(textureOverlayName, HasTransparencyTexture(textureOverlayName)))
            {
                materialToUse = UMAConverterSettings.Instance.TransparentMaterial;
            }

            asset.material = materialToUse;

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

            // Track if this overlay has a transparency texture
            Texture2D transparencyTexture = GetTextureCandidateForSlot("TransparencyMap", overlayName, umaMaterial, searchFolders);
            if (transparencyTexture != null)
            {
                overlayTransparencyMap[overlayName] = true;
            }
            else
            {
                overlayTransparencyMap[overlayName] = false;
            }

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

        private Texture2D GetTextureCandidateForSlot(string channelName, string overlayName, UMAMaterial umaMaterial, string[] searchFolders)
        {
            List<string> candidates = GetTextureCandidatesForChannel(channelName);
            foreach (string candidate in candidates)
            {
                string textureName = overlayName + "_" + candidate;
                Texture texture = FindTextureByName(textureName, searchFolders);
                if (texture != null)
                {
                    return texture as Texture2D;
                }
            }
            return null;
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

        private bool HasTransparencyTexture(string overlayName)
        {
            if (overlayTransparencyMap.TryGetValue(overlayName, out var hasTransparency))
            {
                return hasTransparency;
            }
            return false;
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
            { "OcclusionMap", new List<string> { "Occlusion", "AO", "AmbientOcclusion", "OcclusionMap" } },
            { "ParallaxMap", new List<string> { "Height", "Displacement" } },
            { "SpecGlossMap", new List<string> { "Specular", "SpecGloss", "SpecularGloss" } },
            { "DetailAlbedoMap", new List<string> { "DetailAlbedo", "DetailColor" } },
            { "DetailNormalMap", new List<string> { "DetailNormal", "DetailBump" } },
            { "DetailMask", new List<string>() },
            { "TransparencyMap", new List<string> { "TransparencyMap", "Opacity", "Alpha", "OpacityMask" } },
            { "SpecularMap", new List<string> { "SpecularMap", "SpecGlossMap", "Specular", "SpecGloss", "SpecularGloss" } }
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
            raceData.expressionSet = GenerateExpressionSet();
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
            foreach (UMAData_RaceSlots raceSlot in raceSlots)
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
        // Maps each ExpressionPlayer channel name to the HumanBodyBones that drive it.
        private static readonly Dictionary<string, HumanBodyBones[]> poseChannelBones = new Dictionary<string, HumanBodyBones[]>
        {
            { "neckUp_Down",          new[] { HumanBodyBones.Neck } },
            { "neckLeft_Right",       new[] { HumanBodyBones.Neck } },
            { "neckTiltLeft_Right",   new[] { HumanBodyBones.Neck } },
            { "headUp_Down",          new[] { HumanBodyBones.Head } },
            { "headLeft_Right",       new[] { HumanBodyBones.Head } },
            { "headTiltLeft_Right",   new[] { HumanBodyBones.Head } },
            { "jawOpen_Close",        new[] { HumanBodyBones.Jaw } },
            { "jawForward_Back",      new[] { HumanBodyBones.Jaw } },
            { "jawLeft_Right",        new[] { HumanBodyBones.Jaw } },
            { "leftEyeOpen_Close",    new[] { HumanBodyBones.LeftEye } },
            { "leftEyeUp_Down",       new[] { HumanBodyBones.LeftEye } },
            { "leftEyeIn_Out",        new[] { HumanBodyBones.LeftEye } },
            { "rightEyeOpen_Close",   new[] { HumanBodyBones.RightEye } },
            { "rightEyeUp_Down",      new[] { HumanBodyBones.RightEye } },
            { "rightEyeIn_Out",       new[] { HumanBodyBones.RightEye } },
            { "leftGrasp",            new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal } },
            { "rightGrasp",           new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal } },
            { "leftPeace",            new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal } },
            { "rightPeace",           new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal } },
            { "leftPoint",            new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal } },
            { "rightPoint",           new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal } },
            { "leftRude",             new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal } },
            { "rightRude",            new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal } },
        };

        /// <summary>
        /// Returns the actual rig bone name for a given HumanBodyBones value using the mesh's humanDescription.
        /// Returns null if the bone is not mapped.
        /// </summary>
        private string GetBoneNameForHumanBone(HumanBodyBones humanBone)
        {
            ModelImporter importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
            if (importer == null) return null;
            string humanBoneName = HumanTrait.BoneName[(int)humanBone];
            foreach (HumanBone hb in importer.humanDescription.human)
            {
                if (string.Equals(hb.humanName, humanBoneName, System.StringComparison.Ordinal))
                    return hb.boneName;
            }
            return null;
        }

        /// <summary>
        /// Populates a UMABonePose with identity-delta PoseBones for each humanoid bone that drives
        /// the given expression channel. The delta values (zero position, identity rotation, scale 1)
        /// mean "no change from rest pose" and can be adjusted in the Inspector.
        /// </summary>
        private void PopulateBonePose(UMABonePose bonePose, string channelName)
        {
            HumanBodyBones[] humanBones;
            if (!poseChannelBones.TryGetValue(channelName, out humanBones)) return;

            List<UMABonePose.PoseBone> poseBones = new List<UMABonePose.PoseBone>();
            foreach (HumanBodyBones humanBone in humanBones)
            {
                string boneName = GetBoneNameForHumanBone(humanBone);
                if (string.IsNullOrEmpty(boneName)) continue;

                UMABonePose.PoseBone poseBone = new UMABonePose.PoseBone();
                poseBone.bone = boneName;
                poseBone.hash = UMAUtils.StringToHash(boneName);
                poseBone.position = Vector3.zero;
                poseBone.rotation = Quaternion.identity;
                poseBone.scale = Vector3.one;
                poseBone.category = channelName;
                poseBones.Add(poseBone);
            }

            bonePose.poses = poseBones.ToArray();
        }

        /// <summary>
        /// Generates a UMAExpressionSet for the Race along with an empty UMABonePose asset for every
        /// expression channel (primary and inverse). The pose assets are saved to the Expressions folder
        /// and linked into the matching posePairs slot so the set is immediately usable. Bone data can then
        /// be added to each pose asset by hand or via a pose-capture workflow.
        /// </summary>
        /// <returns>The created UMAExpressionSet asset.</returns>
        /// <exception cref="System.Exception">Thrown if called for a non-race type.</exception>
        public UMAExpressionSet GenerateExpressionSet()
        {
            if (this.data.type != UMADataType.race) throw new System.Exception("GenerateExpressionSet can only be called for Race creation");

            string expressionsFolder = workingDirectory + "/Expressions";
            string posesFolder = expressionsFolder + "/Poses";
            if (!Directory.Exists(posesFolder)) Directory.CreateDirectory(posesFolder);
            AssetDatabase.Refresh();

            string raceName = (this.data as UMAData_Race).name;
            string expressionSetPath = expressionsFolder + "/" + raceName + "_ExpressionSet.asset";

            UMAExpressionSet expressionSet = ScriptableObject.CreateInstance<UMAExpressionSet>();
            expressionSet.posePairs = new UMAExpressionSet.PosePair[ExpressionPlayer.PoseCount];

            // Create the expression set asset first so sub-assets can reference it.
            AssetDatabase.CreateAsset(expressionSet, expressionSetPath);

            for (int i = 0; i < ExpressionPlayer.PoseCount; i++)
            {
                string channelName = ExpressionPlayer.PoseNames[i];

                UMABonePose primaryPose = ScriptableObject.CreateInstance<UMABonePose>();
                primaryPose.name = raceName + "_" + channelName + "_primary";
                primaryPose.poses = new UMABonePose.PoseBone[0];
                PopulateBonePose(primaryPose, channelName);
                string primaryPath = posesFolder + "/" + primaryPose.name + ".asset";
                AssetDatabase.CreateAsset(primaryPose, primaryPath);

                UMABonePose inversePose = ScriptableObject.CreateInstance<UMABonePose>();
                inversePose.name = raceName + "_" + channelName + "_inverse";
                inversePose.poses = new UMABonePose.PoseBone[0];
                PopulateBonePose(inversePose, channelName);
                string inversePath = posesFolder + "/" + inversePose.name + ".asset";
                AssetDatabase.CreateAsset(inversePose, inversePath);

                UMAExpressionSet.PosePair pair = new UMAExpressionSet.PosePair();
                pair.primary = primaryPose;
                pair.inverse = inversePose;
                expressionSet.posePairs[i] = pair;
            }

            EditorUtility.SetDirty(expressionSet);
            AssetDatabase.SaveAssets();

            if (addToGlobalLibrary)
            {
                try
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMAExpressionSet), expressionSet);
                }
                catch (System.Exception)
                {
                    // UMAExpressionSet is not registered in the asset indexer — skip silently.
                }
            }

            Debug.Log("[UMAConverter] ExpressionSet created: path='" + expressionSetPath + "', posePairs=" + expressionSet.posePairs.Length + " (each with primary+inverse UMABonePose)");
            return expressionSet;
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
            if (modelImporter != null)
            {
                var asset = ScriptableObject.CreateInstance<UMA.UmaTPose>();
                asset.ReadFromHumanDescription(modelImporter.humanDescription);
                AssetDatabase.CreateAsset(asset, TPosePath);
                return asset;
            }
            else
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
            if (data.type == UMADataType.race)
            {
                GenerateRaceAssets();
            }
            AssetDatabase.SaveAssets();
            if (UMAConverterSettings.Instance.removeMeshAfterCreating)
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