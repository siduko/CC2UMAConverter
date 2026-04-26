import os
import sys
import types
import unittest
import importlib.util
from unittest import mock


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
    def test_derive_texture_family_handles_index_and_compact_suffixes(self):
        self.assertEqual(
            dazconverter._derive_texture_family_from_filename("SW_Toulouse_01.jpg"),
            "SW_Toulouse",
        )
        self.assertEqual(
            dazconverter._derive_texture_family_from_filename("s051Shorts05.jpg"),
            "s051Shorts",
        )
        self.assertEqual(
            dazconverter._derive_texture_family_from_filename("s051ShortsB.jpg"),
            "s051Shorts",
        )
        self.assertEqual(
            dazconverter._derive_texture_family_from_filename("s051ShortsS.jpg"),
            "s051Shorts",
        )

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
        self.assertEqual(
            dazconverter._classify_texture_filename("s051TopB.jpg"),
            "bump",
        )
        self.assertEqual(
            dazconverter._classify_texture_filename("s051TopS.jpg"),
            "specular",
        )
        self.assertEqual(
            dazconverter._classify_texture_filename("s051Top05.jpg"),
            "color",
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
        self.assertEqual(
            dazconverter._derive_texture_pattern_from_filename("s051Top05.jpg"),
            "s051Top_[0-9]*",
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

    def test_index_textures_by_family_groups_related_maps(self):
        with mock.patch.object(dazconverter.os, "walk") as mock_walk:
            mock_walk.return_value = [
                (
                    "/tmp",
                    [],
                    [
                        "s051Shorts05.jpg",
                        "s051ShortsB.jpg",
                        "s051ShortsS.jpg",
                        "notes.txt",
                    ],
                )
            ]

            indexed = dazconverter._index_textures_by_family("/tmp")

        self.assertEqual(
            indexed["s051shorts"]["color"],
            os.path.join("/tmp", "s051Shorts05.jpg"),
        )
        self.assertEqual(
            indexed["s051shorts"]["bump"],
            os.path.join("/tmp", "s051ShortsB.jpg"),
        )
        self.assertEqual(
            indexed["s051shorts"]["specular"],
            os.path.join("/tmp", "s051ShortsS.jpg"),
        )
        self.assertFalse(
            dazconverter._should_add_color_texture_from_directory(
                {
                    "bump": "/tmp/SW_Toulouse_01B.jpg",
                    "transparency": "/tmp/SW_ToulouseT_Base_TR.jpg",
                }
            )
        )

    def test_index_textures_by_family_groups_compact_suffix_before_number(self):
        # RyJeane-style naming: {char}_{bodypart}{SUFFIX}_{number}.ext
        # armsB_1004 → bump map for arms; armsS_1004 → specular map for arms.
        # All variants must land in one family key with correct map types.
        with mock.patch.object(dazconverter.os, "walk") as mock_walk:
            mock_walk.return_value = [
                (
                    "/tmp",
                    [],
                    [
                        "RyJeane_arms_1004.jpg",   # base color
                        "RyJeane_armsB_1004.jpg",  # bump
                        "RyJeane_armsS_1004.jpg",  # specular
                    ],
                )
            ]
            indexed = dazconverter._index_textures_by_family("/tmp")

        family = indexed.get("ryjeane_arms") or indexed.get("RyJeane_arms")
        self.assertIsNotNone(family, f"Expected 'ryjeane_arms' in indexed keys; got: {list(indexed.keys())}")
        self.assertEqual(len(indexed), 1, f"Expected 1 family key; got {list(indexed.keys())}")
        self.assertIn("color", family)
        self.assertIn("bump", family)
        self.assertIn("specular", family)
        self.assertEqual(family["color"],   os.path.join("/tmp", "RyJeane_arms_1004.jpg"))
        self.assertEqual(family["bump"],    os.path.join("/tmp", "RyJeane_armsB_1004.jpg"))
        self.assertEqual(family["specular"], os.path.join("/tmp", "RyJeane_armsS_1004.jpg"))

    def test_index_textures_by_family_groups_unknown_compact_suffix_by_prefix(self):
        # Unknown suffix token before number should still stay in the same family
        # when the longest shared prefix clearly matches the base texture name.
        with mock.patch.object(dazconverter.os, "walk") as mock_walk:
            mock_walk.return_value = [
                (
                    "/tmp",
                    [],
                    [
                        "RyJeane_arms_1004.jpg",
                        "RyJeane_armsX_1004.jpg",
                    ],
                )
            ]
            indexed = dazconverter._index_textures_by_family("/tmp")

        self.assertIn("ryjeane_arms", indexed)
        self.assertEqual(
            len(indexed), 1, f"Expected one family bucket, got {list(indexed.keys())}"
        )

    def test_index_textures_by_family_regroups_ambiguous_compact_suffix(self):
        # CharacterSkinN.jpg: the existing regex strips the trailing 'N' from
        # "CharacterSkin" because it looks like a compact normal-map suffix,
        # yielding candidate family "characterski" instead of "characterskin".
        # The two-pass algorithm must detect that "characterskin" (with ≥2 members)
        # is a prefix of the stem "characterskin" and re-group correctly.
        with mock.patch.object(dazconverter.os, "walk") as mock_walk:
            mock_walk.return_value = [
                (
                    "/tmp",
                    [],
                    [
                        "CharacterSkin.jpg",    # base color / unknown
                        "CharacterSkinB.jpg",   # bump
                        "CharacterSkinN.jpg",   # normal (over-stripped → wrong candidate)
                    ],
                )
            ]
            indexed = dazconverter._index_textures_by_family("/tmp")

        # All three files must land in one family key; the over-stripped key must be gone.
        self.assertIn("characterskin", indexed)
        self.assertNotIn("characterski", indexed)
        self.assertEqual(
            indexed["characterskin"]["bump"],
            os.path.join("/tmp", "CharacterSkinB.jpg"),
        )
        # Both CharacterSkin.jpg and CharacterSkinN.jpg are typed "normal" by the
        # classifier; whichever arrives first wins the slot.  The important thing is
        # the key "characterski" is gone and "bump" is correctly placed.
        self.assertIn("normal", indexed["characterskin"])

    def test_add_texture_with_manual_fallback_calls_callback_on_failure(self):
        class MaterialStub:
            name = "Trim.001"

        calls = []

        def failing_add(*_args):
            raise RuntimeError("link failure")

        def manual_callback(material, texture_path, texture_type, error_message):
            calls.append((material.name, texture_path, texture_type, error_message))
            return True

        result = dazconverter._add_texture_with_manual_fallback(
            MaterialStub(),
            "/tmp/s051Shorts05.jpg",
            "color",
            skip_manual_mapping=False,
            manual_mapping_callback=manual_callback,
            add_texture_func=failing_add,
        )

        self.assertTrue(result)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][0], "Trim.001")
        self.assertEqual(calls[0][2], "color")
        self.assertIn("link failure", calls[0][3])

    def test_add_texture_with_manual_fallback_skips_when_configured(self):
        class MaterialStub:
            name = "Trim.001"

        callback_called = {"value": False}

        def failing_add(*_args):
            raise RuntimeError("link failure")

        def manual_callback(*_args):
            callback_called["value"] = True
            return True

        result = dazconverter._add_texture_with_manual_fallback(
            MaterialStub(),
            "/tmp/s051Shorts05.jpg",
            "color",
            skip_manual_mapping=True,
            manual_mapping_callback=manual_callback,
            add_texture_func=failing_add,
        )

        self.assertFalse(result)
        self.assertFalse(callback_called["value"])


if __name__ == "__main__":
    unittest.main()