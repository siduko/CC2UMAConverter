import os
import sys
import types
import unittest
import importlib.util


sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

sys.modules.setdefault("bpy", types.SimpleNamespace())
sys.modules.setdefault("bmesh", types.SimpleNamespace())

package_dir = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "../DazUMAConverterBlender")
)

package = types.ModuleType("DazUMAConverterBlender")
package.__path__ = [package_dir]
sys.modules["DazUMAConverterBlender"] = package

data_spec = importlib.util.spec_from_file_location(
    "DazUMAConverterBlender.dataHandling",
    os.path.join(package_dir, "dataHandling.py"),
)
data_module = importlib.util.module_from_spec(data_spec)
sys.modules["DazUMAConverterBlender.dataHandling"] = data_module
data_spec.loader.exec_module(data_module)

spec = importlib.util.spec_from_file_location(
    "DazUMAConverterBlender.dazconverter",
    os.path.join(package_dir, "dazconverter.py"),
)
dazconverter = importlib.util.module_from_spec(spec)
sys.modules["DazUMAConverterBlender.dazconverter"] = dazconverter
spec.loader.exec_module(dazconverter)


class TestDazConverterPatternConfig(unittest.TestCase):
    def test_color_patterns_include_diffuse_suffix(self):
        color_patterns = dazconverter._resolve_color_map_patterns(
            dazconverter._MATERIAL_TEXTURE_MAP["Eyelashes"]
        )

        self.assertEqual(
            color_patterns,
            [
                "*_lashes_[0-9]*",
                "*_lashesD_[0-9]*",
                "*_lashes*diffuse*",
                "*_lashes*diffuse*_[0-9]*",
            ],
        )

    def test_additional_maps_include_roughness_and_metallic(self):
        arms_maps = dazconverter._MATERIAL_ADDITIONAL_MAPS["Arms"]

        self.assertIn("roughness", arms_maps)
        self.assertIn("metallic", arms_maps)

        roughness_patterns = dazconverter._resolve_additional_map_patterns(
            dazconverter._MATERIAL_TEXTURE_MAP["Arms"],
            arms_maps["roughness"],
        )
        metallic_patterns = dazconverter._resolve_additional_map_patterns(
            dazconverter._MATERIAL_TEXTURE_MAP["Arms"],
            arms_maps["metallic"],
        )

        self.assertEqual(
            roughness_patterns,
            [
                "*_armsR_[0-9]*",
                "*_armsRO_[0-9]*",
                "*_arms*roughness*",
                "*_arms*roughness*_[0-9]*",
            ],
        )
        self.assertEqual(
            metallic_patterns,
            [
                "*_armsM_[0-9]*",
                "*_armsMT_[0-9]*",
                "*_arms*metallic*",
                "*_arms*metallic*_[0-9]*",
            ],
        )

    def test_classify_texture_filename_prefers_expected_types(self):
        self.assertEqual(
            dazconverter._classify_texture_filename("SW_Toulouse_01.jpg"),
            "color",
        )
        self.assertEqual(
            dazconverter._classify_texture_filename("SW_Toulouse_TR1.jpg"),
            "transparency",
        )
        # _01B: digit + variant letter -> color (not bump; bump needs a word separator: _Face_B)
        self.assertEqual(
            dazconverter._classify_texture_filename("SW_Toulouse_01B.jpg"),
            "color",
        )
        # Explicit bump suffix after word separator must still work
        self.assertEqual(
            dazconverter._classify_texture_filename("Character_Face_B.jpg"),
            "bump",
        )
        self.assertEqual(
            dazconverter._classify_texture_filename("Character_Face_BM.jpg"),
            "bump",
        )

    def test_derive_texture_pattern_prefers_indexed_variant(self):
        self.assertEqual(
            dazconverter._derive_texture_pattern_from_filename("SW_Toulouse_01.jpg"),
            "SW_Toulouse_[0-9]*",
        )
        self.assertEqual(
            dazconverter._derive_texture_pattern_from_filename("SW_Toulouse_TR1.jpg"),
            "SW_Toulouse_[0-9]*",
        )

    def test_should_add_color_texture_from_directory(self):
        self.assertTrue(
            dazconverter._should_add_color_texture_from_directory({})
        )
        self.assertTrue(
            dazconverter._should_add_color_texture_from_directory(
                {"color": "/tmp/SW_Toulouse_01.jpg"}
            )
        )
        self.assertFalse(
            dazconverter._should_add_color_texture_from_directory(
                {
                    "bump": "/tmp/SW_Toulouse_01B.jpg",
                    "transparency": "/tmp/SW_ToulouseT_Base_TR.jpg",
                }
            )
        )


if __name__ == "__main__":
    unittest.main()