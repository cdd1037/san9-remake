import unittest

import legacy_text


class LegacyTextTests(unittest.TestCase):
    def test_decodes_ascii_big5_and_controls_without_loss(self) -> None:
        raw = b"PK:" + "三國志".encode("big5") + b"\x02\n"

        segments, had_error, used_extension = legacy_text.decode_big5_segments(raw)

        self.assertFalse(had_error)
        self.assertFalse(used_extension)
        self.assertEqual(
            legacy_text.display_text_from_segments(segments),
            "PK:三國志<0x02>\n",
        )

    def test_strict_mode_rejects_hkscs_only_pair(self) -> None:
        pair = None
        for lead in range(0x81, 0xFF):
            for trail in range(0x40, 0xFF):
                value = bytes((lead, trail))
                try:
                    value.decode("big5hkscs")
                except UnicodeDecodeError:
                    continue
                try:
                    value.decode("big5")
                except UnicodeDecodeError:
                    pair = value
                    break
            if pair is not None:
                break
        self.assertIsNotNone(pair)

        _, strict_error, strict_extension = legacy_text.decode_big5_segments(
            pair or b"",
            allow_hkscs=False,
        )
        _, fallback_error, fallback_extension = legacy_text.decode_big5_segments(
            pair or b"",
            allow_hkscs=True,
        )

        self.assertTrue(strict_error)
        self.assertFalse(strict_extension)
        self.assertFalse(fallback_error)
        self.assertTrue(fallback_extension)

    def test_segment_conversion_does_not_change_controls(self) -> None:
        segments = [
            {"kind": "text", "value": "傳統"},
            {"kind": "control", "hex": "02 DA"},
        ]

        converted = legacy_text.convert_text_segments(
            segments,
            lambda value: value.replace("傳統", "传统"),
        )

        self.assertEqual(converted[0]["value"], "传统")
        self.assertEqual(converted[1], {"kind": "control", "hex": "02 DA"})
        self.assertEqual(segments[0]["value"], "傳統")

    def test_cp950_profile_matches_windows_big5_punctuation(self) -> None:
        text, had_error, used_extension = legacy_text.render_big5(
            bytes.fromhex("A1 45"),
            allow_hkscs=False,
            primary_encoding="cp950",
        )

        self.assertEqual(text, "‧")
        self.assertFalse(had_error)
        self.assertFalse(used_extension)

    def test_scans_only_complete_strict_big5_c_strings(self) -> None:
        first = "三國志ⅨPK".encode("big5")
        second = "出征部隊".encode("big5")
        data = b"\0" + first + b"\0" + b"\x81\x30\0" + second + b"\0tail"

        candidates = legacy_text.scan_null_terminated_big5(data)

        self.assertEqual([candidate.text for candidate in candidates], ["三國志ⅨPK", "出征部隊"])
        self.assertEqual(candidates[0].offset, 1)
        self.assertEqual(candidates[0].raw, first)

if __name__ == "__main__":
    unittest.main()
