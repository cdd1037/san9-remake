import json
import struct
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from verify_texture_library import verify


class TextureLibraryVerificationTests(unittest.TestCase):
    def fixture(self, root):
        source = root / "source"
        package = root / "package"
        source.mkdir()
        package.mkdir()
        palette = bytes([10, 20, 30, 128, 40, 50, 60, 128]) + bytes(1016)
        record = struct.pack("<4I", 18, 2, 2, 1) + bytes([0, 1])
        (source / "G_Test.s9").write_bytes(struct.pack("<I", 1) + bytes(40) + palette + struct.pack("<I", 22) + record)
        Image.frombytes("RGBA", (2, 1), bytes([40, 50, 60, 0, 40, 50, 60, 255])).save(package / "rgb.png")

        def asset(path):
            return {"id": path, "file": {"path": path}, "x": 0, "y": 0, "width": 2, "height": 1}

        manifest = {"library": {"archives": [{"id": "g_test", "category": "interface/test",
                    "variants": [{"imageIndex": 0, "paletteIndex": 0, "image": asset("rgb.png")}]}]}}
        (package / "textures.json").write_text(json.dumps(manifest), encoding="utf-8")
        return source, package

    def test_all_source_pixels_and_colors_match(self):
        with tempfile.TemporaryDirectory() as folder:
            source, package = self.fixture(Path(folder))
            report = verify(package, source)
            self.assertEqual((1, 1), (report["archives"], report["rgb_variants"]))

    def test_wrong_color_output_is_detected(self):
        with tempfile.TemporaryDirectory() as folder:
            source, package = self.fixture(Path(folder))
            Image.new("RGB", (2, 1), "red").save(package / "rgb.png")
            with self.assertRaises(AssertionError):
                verify(package, source)

    def test_omitted_original_archive_is_detected(self):
        with tempfile.TemporaryDirectory() as folder:
            source, package = self.fixture(Path(folder))
            (source / "G_Extra.s9").write_bytes((source / "G_Test.s9").read_bytes())
            with self.assertRaises(AssertionError):
                verify(package, source)


if __name__ == "__main__":
    unittest.main()
