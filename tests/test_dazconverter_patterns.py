import os
import sys
import types
import unittest


sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

sys.modules.setdefault("bpy", types.SimpleNamespace())
sys.modules.setdefault("bmesh", types.SimpleNamespace())

from DazUMAConverterBlender import dazconverter  # noqa: E402


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
            ["*_armsR_[0-9]*", "*_armsRO_[0-9]*", "*_arms*roughness*_[0-9]*"],
        )
        self.assertEqual(
            metallic_patterns,
            ["*_armsM_[0-9]*", "*_armsMT_[0-9]*", "*_arms*metallic*_[0-9]*"],
        )


if __name__ == "__main__":
    unittest.main()