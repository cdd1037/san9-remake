import unittest
from audit_font_extensions import decode, glyph, private_index, font_slots

class FontExtensionTests(unittest.TestCase):
    def test_private_range_is_not_the_font_fa40_range(self):
        self.assertIsNone(private_index(0xFA40))
        self.assertIsNone(private_index(0xFAAF))
        self.assertEqual(private_index(0xFAB0), 0)
        self.assertEqual(private_index(0xFAFC), 76)
        self.assertIsNone(private_index(0xFAFD))

    def test_low_nibble_is_left_pixel(self):
        self.assertEqual(list(glyph(bytes([0xF0, 0xA5]), 0, 2, 0).tobytes()), [255, 0, 170, 85])
        with self.assertRaises(ValueError):
            glyph(bytes([0]), 0, 2, 0)

    def test_hkscs_projection_is_not_cp950(self):
        self.assertIsNone(decode(0xFA41, "cp950"))
        self.assertEqual(decode(0xFA41, "big5hkscs"), "鋛")
        with self.assertRaises(ValueError):
            font_slots(bytes(16))
