import struct
import unittest

from analyze_scenario_persons import RECORD_COUNT, RECORD_SIZE, profile_payload


class PersonProfileTests(unittest.TestCase):
    def test_reports_raw_outliers_and_identity_without_normalizing(self):
        payload = bytearray(RECORD_COUNT * RECORD_SIZE)
        for index in range(RECORD_COUNT):
            struct.pack_into("<H", payload, index * RECORD_SIZE + 4, index)
        struct.pack_into("<H", payload, 4, 799)
        struct.pack_into("<H", payload, 0x1E, 65535)
        struct.pack_into("<H", payload, 0x38, 1200)
        struct.pack_into("<I", payload, 0, 0x80000002)
        payload[0x85] = 0x80
        payload[0x4E] = 255
        result = profile_payload(bytes(payload))
        self.assertEqual([{"record_index": 0, "serialized_id": 799}], result["serialized_id_mismatches"])
        self.assertEqual([799], result["duplicate_serialized_ids"])
        self.assertEqual(65535, result["fields"]["leadership"]["max"])
        self.assertEqual(1200, result["fields"]["proficiency"]["max"])
        self.assertEqual(255, result["fields"]["loyalty"]["max"])
        self.assertEqual("0x80000002", result["flags_or"])
        self.assertEqual("0x800000000000", result["tactics_or"])

    def test_rejects_incomplete_and_extra_records(self):
        for delta in (-1, 1, RECORD_SIZE):
            with self.subTest(delta=delta), self.assertRaises(ValueError):
                profile_payload(bytes(RECORD_COUNT * RECORD_SIZE + delta))


if __name__ == "__main__":
    unittest.main()
