import json
import tempfile
import unittest
from pathlib import Path

from build_program_analysis_close01 import PRE_CLOSE_BATCHES, build_status


class BuildProgramAnalysisClose01Tests(unittest.TestCase):
    def test_closes_old_routes_and_preserves_close02(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            roadmap = root / "roadmap.md"
            roadmap.write_text("\n".join(
                [*(f"| `{batch}` | `completed` | x | y |" for batch in PRE_CLOSE_BATCHES),
                 "| `CLOSE-01` | `in-progress` | x | y |", "| `CLOSE-02` | `planned` | x | y |"]), encoding="utf-8")
            coverage = root / "coverage.md"
            coverage.write_text("| `CLAIM-1` | source | topic | `confirmed` | `completed:EVT-02` | evidence |\n", encoding="utf-8")
            queue = root / "queue.json"
            queue.write_text(json.dumps({
                "candidate_count": 7,
                "disposition_counts": {"baseline": 2, "out-of-scope": 3},
                "final_round_routing": {"batch_counts": {"EVT-02": 1, "CLOSE-02": 1}, "unrouted_count": 0},
                "validation": {"valid": True, "issues": {}},
            }), encoding="utf-8")
            evt01 = root / "evt01.json"
            evt01.write_text(json.dumps({
                "source_claims": {"routes": {"CLAIM-1": "EVT-02"}},
                "existing_findings": {"routes": [
                    {"finding": "RE-1", "disposition": "roadmap", "batch": "EVT-02"},
                    {"finding": "RE-2", "disposition": "roadmap", "batch": "CLOSE-02"},
                ]},
            }), encoding="utf-8")
            result = build_status(roadmap, coverage, queue, evt01)
        self.assertTrue(result["validation"]["valid"])
        self.assertEqual(result["san9pkhard_candidates"]["current_roadmap_by_batch"], {"CLOSE-02": 1})
        self.assertEqual(result["existing_findings"]["current_roadmap_by_batch"], {"CLOSE-02": 1})

    def test_rejects_stale_planned_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            roadmap = root / "roadmap.md"
            roadmap.write_text("\n".join(
                [*(f"| `{batch}` | `completed` | x | y |" for batch in PRE_CLOSE_BATCHES),
                 "| `CLOSE-01` | `in-progress` | x | y |"]), encoding="utf-8")
            coverage = root / "coverage.md"
            coverage.write_text("| `OLD-1` | source | topic | `planned` | `roadmap:EVT-02` | evidence |\n", encoding="utf-8")
            queue = root / "queue.json"
            queue.write_text(json.dumps({
                "candidate_count": 1, "disposition_counts": {"baseline": 1, "out-of-scope": 0},
                "final_round_routing": {"batch_counts": {}, "unrouted_count": 0},
                "validation": {"valid": True, "issues": {}},
            }), encoding="utf-8")
            evt01 = root / "evt01.json"
            evt01.write_text(json.dumps({"source_claims": {"routes": {}}, "existing_findings": {"routes": []}}), encoding="utf-8")
            result = build_status(roadmap, coverage, queue, evt01)
        self.assertFalse(result["validation"]["valid"])
        self.assertEqual(result["coverage"]["stale_planned_rows"], ["OLD-1"])


if __name__ == "__main__":
    unittest.main()
