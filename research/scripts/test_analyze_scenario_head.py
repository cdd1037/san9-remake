import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import analyze_scenario_head as head


class AnalyzeScenarioHeadTests(unittest.TestCase):
    def test_date_initialized_mask_uses_inclusive_thresholds(self) -> None:
        before = head.encode_date(192, 3, 30)
        first = head.encode_date(192, 4, 1)
        second = head.encode_date(192, 5, 1)

        self.assertEqual(0, head.date_initialized_mask(before))
        self.assertEqual(1 << 1, head.date_initialized_mask(first))
        self.assertEqual((1 << 1) | (1 << 2), head.date_initialized_mask(second))

    def test_head_layout_is_exactly_82_bytes(self) -> None:
        payload = struct.pack(
            "<5I6H50s",
            head.FORMAT_VERSION,
            0,
            0,
            0,
            0,
            1,
            0,
            head.encode_date(190, 1, 1),
            36,
            200,
            200,
            bytes([0xFF] * 50),
        )

        self.assertEqual(82, len(payload))


if __name__ == "__main__":
    unittest.main()
