using UnityEngine;
using UnityEditor;
using System.IO;
using UMA;
using UMA.PoseTools;
namespace UMAConverter
{
    [CreateAssetMenu(fileName = "UMAConverterSettings", menuName = "UMA/Converter Settings")]
    public class UMAConverterSettings : ScriptableObject
    {
        private static readonly string[] DefaultMaterialCandidatePaths = new string[]
        {
            "Packages/com.ovstudio.umaconverter/Runtime/UMAMaterials/CCMaterial.asset",
            "Packages/com.vwgamedev.umaconverter/Runtime/UMAMaterials/CCMaterial.asset",
            "Assets/Runtime/UMAMaterials/CCMaterial.asset"
        };

        private static readonly string[] TransparentMaterialCandidatePaths = new string[]
        {
            "Packages/com.ovstudio.umaconverter/Runtime/UMAMaterials/CCTransparentMaterial.asset",
            "Packages/com.vwgamedev.umaconverter/Runtime/UMAMaterials/CCTransparentMaterial.asset",
            "Assets/Runtime/UMAMaterials/CCTransparentMaterial.asset"
        };


        public bool addToGlobalLibrary = true;
        public UMAMaterial defaultMaterial = null;
        [SerializeField]
        private UMAMaterial transparentMaterial;
        [Header("DNA")]
        [SerializeField]
        private ScriptableObject referenceDynamicDnaAsset;
        [SerializeField]
        private ScriptableObject referenceDnaConverterController;
        [SerializeField]
        private ScriptableObject referenceDynamicDnaRanges;
        [Header("Expressions")]
        [Tooltip("When set, pose bone transforms will be copied from this expression set instead of being left as identity. Use an expression set from another character of the same rig type.")]
        [SerializeField]
        private UMAExpressionSet referenceExpressionSet;
        public bool removeMeshAfterCreating = false;

#if UMAConverterGCInventory
        [Header("Game Creator")]
        public bool CreateItems = false;
        public string ParentItemLocation = "Assets/Items/CharacterSlots";
#endif


        public void OnEnable()
        {
            EnsureMaterialReferences();
        }

        private static UMAConverterSettings instance;

        public static UMAConverterSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindOrCreateInstance();
                }

                instance.EnsureMaterialReferences();
                return instance;
            }
        }

        public UMAMaterial TransparentMaterial
        {
            get
            {
                EnsureMaterialReferences();
                return transparentMaterial;
            }
        }

        public ScriptableObject ReferenceDynamicDnaAsset
        {
            get { return referenceDynamicDnaAsset; }
            set { referenceDynamicDnaAsset = value; }
        }

        public ScriptableObject ReferenceDnaConverterController
        {
            get { return referenceDnaConverterController; }
            set { referenceDnaConverterController = value; }
        }

        public ScriptableObject ReferenceDynamicDnaRanges
        {
            get { return referenceDynamicDnaRanges; }
            set { referenceDynamicDnaRanges = value; }
        }

        public UMAExpressionSet ReferenceExpressionSet
        {
            get { return referenceExpressionSet; }
            set { referenceExpressionSet = value; }
        }

        private static UMAConverterSettings FindOrCreateInstance()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UMAConverterSettings>("Assets/Settings/UMAConverterSettings.asset");
            if (settings == null)
            {
                settings = CreateInstance<UMAConverterSettings>();

                if (!Directory.Exists("Assets/Settings"))
                {
                    AssetDatabase.CreateFolder("Assets", "Settings");
                }

                AssetDatabase.CreateAsset(settings, "Assets/Settings/UMAConverterSettings.asset");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return settings;
        }

        private void EnsureMaterialReferences()
        {
            if (defaultMaterial == null)
            {
                defaultMaterial = LoadFirstMaterial(DefaultMaterialCandidatePaths);
            }

            if (transparentMaterial == null)
            {
                transparentMaterial = LoadFirstMaterial(TransparentMaterialCandidatePaths);
            }
        }

        private static UMAMaterial LoadFirstMaterial(string[] candidatePaths)
        {
            foreach (string candidatePath in candidatePaths)
            {
                UMAMaterial material = AssetDatabase.LoadAssetAtPath<UMAMaterial>(candidatePath);
                if (material != null)
                {
                    return material;
                }
            }

            return null;
        }
    }
}