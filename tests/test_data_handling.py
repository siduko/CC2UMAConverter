import unittest
import os
import sys
import importlib.util

# Add module path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

# Test dataHandling directly by importing the file directly
spec = importlib.util.spec_from_file_location("dataHandling", os.path.abspath(os.path.join(os.path.dirname(__file__), '../UMAConverterBlender/dataHandling.py')))
dataHandling = importlib.util.module_from_spec(spec)

import unittest.mock
sys.modules['bpy'] = unittest.mock.MagicMock()

spec.loader.exec_module(dataHandling)

UMAData_Race = dataHandling.UMAData_Race
UMAData_Cloth = dataHandling.UMAData_Cloth
UMAData_Slot = dataHandling.UMAData_Slot
UMAData_Overlay = dataHandling.UMAData_Overlay
save_to_json_file = dataHandling.save_to_json_file
load_from_json_file = dataHandling.load_from_json_file

class TestDataHandling(unittest.TestCase):
    def tearDown(self):
        if os.path.exists("test_race.json"):
            os.remove("test_race.json")
        if os.path.exists("test_cloth.json"):
            os.remove("test_cloth.json")

    def test_save_and_load_race_data(self):
        race = UMAData_Race("TestRace", 1.0, ["mesh1"], ["overlay1"], [])
        save_to_json_file(race, "test_race.json")
        self.assertTrue(os.path.exists("test_race.json"))

        loaded_race = load_from_json_file("test_race.json", UMAData_Race)
        self.assertEqual(loaded_race.name, "TestRace")
        self.assertEqual(loaded_race.hipHeight, 1.0)
        self.assertEqual(loaded_race.meshes, ["mesh1"])

    def test_save_and_load_cloth_data(self):
        cloth = UMAData_Cloth(["TestRace"], ["mesh2"], ["overlay2"], [])
        save_to_json_file(cloth, "test_cloth.json")
        self.assertTrue(os.path.exists("test_cloth.json"))

        loaded_cloth = load_from_json_file("test_cloth.json", UMAData_Cloth)
        self.assertEqual(loaded_cloth.compatibleRaces, ["TestRace"])
        self.assertEqual(loaded_cloth.meshes, ["mesh2"])

if __name__ == '__main__':
    unittest.main()
