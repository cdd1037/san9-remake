import json
import struct
import tempfile
import unittest
from pathlib import Path

from verify_patch_points import verify


class VerifyPatchPointsTests(unittest.TestCase):
    def test_maps_virtual_address_and_reports_match(self):
        data = bytearray(0x400)
        data[:2] = b"MZ"
        struct.pack_into("<I", data, 0x3C, 0x80)
        data[0x80:0x84] = b"PE\0\0"
        struct.pack_into("<H", data, 0x86, 1)
        struct.pack_into("<H", data, 0x94, 0xE0)
        struct.pack_into("<H", data, 0x98, 0x10B)
        struct.pack_into("<I", data, 0x98 + 28, 0x400000)
        section = 0x98 + 0xE0
        struct.pack_into("<IIII", data, section + 8, 0x100, 0x1000, 0x100, 0x200)
        data[0x210:0x212] = b"\x07\xAA"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            image = root / "sample.exe"
            manifest = root / "points.json"
            image.write_bytes(data)
            manifest.write_text(json.dumps({"points": [{"name": "p", "address": "0x401010", "expected_old_hex": "07 AA"}]}), encoding="utf-8")
            report = verify(image, manifest)
        self.assertEqual(report["counts"], {"direct-match": 1, "conflict": 0})
        self.assertEqual(report["points"][0]["actual_hex"], "07 AA")


if __name__ == "__main__":
    unittest.main()
