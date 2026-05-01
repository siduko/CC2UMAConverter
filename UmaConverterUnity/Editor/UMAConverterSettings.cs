using UnityEngine;
using UnityEditor;
using System.IO;
using UMA;
namespace UMAConverter
{
    [CreateAssetMenu(fileName = "UMAConverterSettings", menuName = "UMA/Converter Settings")]
    public class UMAConverterSettings : ScriptableObject
    {

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
        public bool removeMeshAfterCreating = false;

#if UMAConverterGCInventory
        [Header("Game Creator")]
        public bool CreateItems = false;
        public string ParentItemLocation = "Assets/Items/CharacterSlots";
#endif


        public void OnEnable()
        {
            if (defaultMaterial == null)
            {
                defaultMaterial = AssetDatabase.LoadAssetAtPath<UMAMaterial>("Packages/com.vwgamedev.umaconverter/Runtime/UMAMaterials/CCMaterial.asset");
            }
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
                return instance;
            }
        }

        public UMAMaterial TransparentMaterial
        {
            get
            {
                if (transparentMaterial == null)
                {
                    // Try to load from default location
                    transparentMaterial = AssetDatabase.LoadAssetAtPath<UMAMaterial>(
                        "Assets/Runtime/UMAMaterials/CCTransparentMaterial.asset");
                }
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
    }
}