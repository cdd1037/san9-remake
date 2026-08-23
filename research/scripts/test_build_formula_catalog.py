from __future__ import annotations

import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parent))

from build_formula_catalog import extract_formula_sections, extract_patch_initializers, load_patch_manifest


class FormulaCatalogTests(unittest.TestCase):
    def test_extracts_original_and_improvement_without_merging(self) -> None:
        sections = []
        for number in range(30):
            sections.append(f"### {number}.formula {number}\noriginal {number}")
            if number == 3:
                sections.append("#### 优化方法\nimproved 3")
        result = extract_formula_sections("\n".join(sections))
        self.assertEqual(result[3][1].original, "original 3")
        self.assertEqual(result[3][1].improvement, "improved 3")
        self.assertIsNone(result[4][1].improvement)

    def test_extracts_first_patch_initializer_and_line(self) -> None:
        values = extract_patch_initializers(
            "x: m_dwRuleAddr(0x401000), m_dwOldRule(25)\n"
            "m_dwRuleAddr(0x402000)"
        )
        self.assertEqual(values["m_dwRuleAddr"]["value"], 0x401000)
        self.assertEqual(values["m_dwRuleAddr"]["line"], 1)
        self.assertEqual(values["m_dwOldRule"]["value"], 25)

    def test_loads_named_patch_manifest_points(self) -> None:
        import json
        import tempfile

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "points.json"
            path.write_text(json.dumps({"points": [{
                "name": "patchRuleThunk",
                "address": "0x401000",
                "expected_old_hex": "90 90",
            }]}), encoding="utf-8")
            values = load_patch_manifest(path)
        self.assertEqual(values["patchRuleThunk"]["address"], "0x401000")
        self.assertEqual(values["patchRuleThunk"]["expected_old_hex"], "90 90")


if __name__ == "__main__":
    unittest.main()
