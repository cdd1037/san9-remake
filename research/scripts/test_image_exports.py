import struct
import tempfile
import unittest
from pathlib import Path

import export_g_face_palette0 as face
import export_g_still_treasure as still


class FaceExportTests(unittest.TestCase):
    def test_parse_palette_requires_registered_layout(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "P_Face.s9"
            first = bytes(range(256)) * 4
            path.write_bytes(struct.pack("<I", 2) + bytes(40) + first + bytes(40 + 1024))

            self.assertEqual(first, face.parse_palette(path))

            path.write_bytes(struct.pack("<I", 1) + bytes(40 + 1024))
            with self.assertRaises(ValueError):
                face.parse_palette(path)

    def test_parse_image_stream_rejects_stride_width_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "G_FaceS.s9"
            record = struct.pack("<4I", 20, 4, 2, 1) + bytes(4)
            path.write_bytes(struct.pack("<I", 4 + len(record)) + record)

            with self.assertRaises(ValueError):
                face.parse_image_stream(path)


class StillExportTests(unittest.TestCase):
    def test_parse_s9_rejects_stride_width_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "G_Still.s9"
            palette = bytes(still.PALETTE_RECORD_SIZE)
            record = struct.pack("<4I", 20, 4, 2, 1) + bytes(4)
            image_stream = struct.pack("<I", 4 + len(record)) + record
            path.write_bytes(struct.pack("<I", 1) + palette + image_stream)

            with self.assertRaises(ValueError):
                still.parse_s9(path)


if __name__ == "__main__":
    unittest.main()
