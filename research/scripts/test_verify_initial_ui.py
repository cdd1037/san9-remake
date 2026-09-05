import struct
import unittest
from PIL import Image
from verify_initial_ui import parse_combined, verify_frame


class InitialUiAuditTests(unittest.TestCase):
    def test_combined_stream_rejects_wrong_stride_and_length(self):
        image = struct.pack("<4I", 18, 2, 2, 1) + bytes([0, 1])
        data = struct.pack("<I", 1) + bytes(1064) + struct.pack("<I", 22) + image
        palettes, images = parse_combined(data)
        self.assertEqual(1, len(palettes))
        self.assertEqual((2, 1, bytes([0, 1])), images[0])
        with self.assertRaises(ValueError):
            parse_combined(data[:-1])
        wrong = bytearray(data)
        struct.pack_into("<I", wrong, 4 + 1064 + 4 + 4, 3)
        with self.assertRaises(ValueError):
            parse_combined(wrong)

    def test_frames_check_visible_color_and_alpha_but_allow_hidden_color_extension(self):
        palette = bytes([0, 248, 0, 0, 50, 80, 120, 128]) + bytes(1016)
        source = (4, 1, bytes([0, 1, 1, 0]))
        image = Image.frombytes("RGBA", (2, 1), bytes([50, 80, 120, 0, 50, 80, 120, 255]))
        verify_frame(image, source, palette, 2, 0)
        image.putpixel((1, 0), (50, 80, 121, 255))
        with self.assertRaises(AssertionError):
            verify_frame(image, source, palette, 2, 0)


if __name__ == "__main__":
    unittest.main()
