import unittest

import parse_pk_text as parser


class BbcodeProjectionTests(unittest.TestCase):
    def render(self, payload: bytes) -> tuple[str, dict[str, object]]:
        report = parser._new_bbcode_report(parser.Path("M_MsgPK.s9"))
        text = parser.build_bbcode_record(
            payload,
            record_index=7444,
            record_offset=0x56947,
            converter=None,
            report=report,
        )
        return text, report

    def test_record_7444_semantics_and_literal_newline(self) -> None:
        payload = bytes.fromhex(
            "02 01 DA 0A A6 A8 AC B0 02 33 79 AA BA A4 6A B1 4E A4 46 A1 43"
        )
        text, report = self.render(payload)

        self.assertEqual(
            text,
            "{{person[0].name}}\n"
            "成為{{unit[0].related_location}}的大將了。",
        )
        self.assertEqual(report["categories"], {})

    def test_runtime_effect_projection_is_explicit(self) -> None:
        payload = bytes.fromhex(
            "02 01 DA 0A A6 A8 AC B0 02 33 79 AA BA A4 6A B1 4E A4 46 A1 43"
        )
        report = parser._new_bbcode_report(parser.Path("M_MsgPK.s9"))
        text = parser.build_bbcode_record(
            payload,
            record_index=7444,
            record_offset=0x56947,
            converter=None,
            report=report,
            include_runtime_effects=True,
        )
        self.assertEqual(
            text,
            "[color=#98B0F8]{{person[0].name}}[/color]\n"
            "成為[color=#88F880]{{unit[0].related_location}}[/color]的大將了。",
        )

    def test_inline_b_styles_become_color_tags(self) -> None:
        payload = bytes.fromhex("02 8C 31 62 A4 6A 02 8C 33 32 62 A4 41")
        text, report = self.render(payload)

        self.assertEqual(text, "[color=#98B0F8]大[/color]乙")
        categories = list(report["categories"].values())
        self.assertEqual(len(categories), 1)
        self.assertTrue(all(category["status"] == "lossy" for category in categories))

    def test_unresolved_macro_is_preserved_and_reported(self) -> None:
        payload = bytes.fromhex("01 4A 34 12 A4 6A")
        text, report = self.render(payload)

        self.assertEqual(text, "{{meta.macro:01 4a 34 12}}大")
        category = next(iter(report["categories"].values()))
        self.assertEqual(category["kind"], "macro")
        self.assertEqual(category["status"], "unresolved")

    def test_legacy_projection_remains_control_visible(self) -> None:
        text, _, _ = parser.render_big5(bytes.fromhex("02 01 DA 0A A6 A8"))
        self.assertEqual(text, "<0x02><0x01><0xDA>\n成")


if __name__ == "__main__":
    unittest.main()
