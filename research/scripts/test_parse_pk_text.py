import unittest

import parse_pk_text as parser


class LegacyProjectionTests(unittest.TestCase):
    def test_control_bytes_remain_visible(self) -> None:
        text, _, _ = parser.render_big5(bytes.fromhex("02 01 DA 0A A6 A8"))
        self.assertEqual(text, "<0x02><0x01><0xDA>\n成")


if __name__ == "__main__":
    unittest.main()
