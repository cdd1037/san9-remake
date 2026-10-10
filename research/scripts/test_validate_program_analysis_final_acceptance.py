import tempfile
import unittest
from pathlib import Path

from validate_program_analysis_final_acceptance import completed_disposition, markdown_link_audit, roadmap_batches, write_record_is_verified


class ValidateProgramAnalysisFinalAcceptanceTests(unittest.TestCase):
    def test_compound_disposition_requires_every_assigned_batch_to_be_complete(self):
        batches = {"PERS-04": "completed", "GOV-04": "completed", "WAR-04": "planned"}
        self.assertTrue(completed_disposition("completed:PERS-04+GOV-04", batches))
        self.assertFalse(completed_disposition("completed:PERS-04+WAR-04", batches))
        self.assertFalse(completed_disposition("completed:PERS-04+MISSING-01", batches))
        self.assertFalse(completed_disposition("completed:PERS-04+", batches))

    def test_parses_completed_roadmap_rows_and_rejects_duplicates(self):
        text = "| `COV-01` | `completed` | x | y |\n| `DOM-01` | `completed` | x | y |\n"
        self.assertEqual(roadmap_batches(text), {"COV-01": "completed", "DOM-01": "completed"})
        with self.assertRaises(ValueError):
            roadmap_batches(text + "| `COV-01` | `planned` | x | y |\n")

    def test_write_record_requires_save_readback_and_unchanged_bytes(self):
        valid, problems = write_record_is_verified({"saved": True, "readback_verified": True, "program_byte_changes": 0})
        self.assertTrue(valid)
        self.assertEqual(problems, [])
        valid, problems = write_record_is_verified({"saved": False, "readback_verified": False, "sample_bytes_modified": True})
        self.assertFalse(valid)
        self.assertEqual(len(problems), 3)

    def test_markdown_link_audit_resolves_percent_encoded_local_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "with space.md"
            target.write_text("ok", encoding="utf-8")
            source = root / "source.md"
            source.write_text("[ok](with%20space.md) [missing](none.md)", encoding="utf-8")
            count, missing = markdown_link_audit([source])
        self.assertEqual(count, 2)
        self.assertEqual(len(missing), 1)
        self.assertEqual(missing[0]["target"], "none.md")


if __name__ == "__main__":
    unittest.main()
