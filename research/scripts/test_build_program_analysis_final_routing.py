import json
import tempfile
import unittest
from pathlib import Path

from build_program_analysis_final_routing import FINDING_ROUTES, SOURCE_CLAIM_ROUTES, build_status


class BuildProgramAnalysisFinalRoutingTests(unittest.TestCase):
    def test_conserves_pending_findings_and_validates_claims(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            findings = root / "findings"
            findings.mkdir()
            for finding_id in FINDING_ROUTES:
                (findings / f"{finding_id}.md").write_text("## 待验证\n", encoding="utf-8")
            (findings / "RE-SAN9PK-0127.md").write_text("## 待验证\n", encoding="utf-8")
            coverage = root / "coverage.md"
            claims = (
                f"| `{claim_id}` | source | topic | `planned` | `roadmap:{batch}` | gap |\n"
                for claim_id, batch in SOURCE_CLAIM_ROUTES.items()
            )
            coverage.write_text("".join(claims), encoding="utf-8")
            queue = root / "queue.json"
            queue.write_text(json.dumps({
                "candidate_count": 3,
                "queue_sha256": "A" * 64,
                "disposition_counts": {"baseline": 1, "out-of-scope": 1},
                "final_round_routing": {"candidate_count": 1, "batch_counts": {"EVT-02": 1}, "unrouted_count": 0},
            }), encoding="utf-8")
            result = build_status(findings, coverage, queue)
        self.assertTrue(result["validation"]["valid"])
        self.assertEqual(result["existing_findings"]["pending_marker_finding_count"], len(FINDING_ROUTES))
        self.assertNotIn("RE-SAN9PK-0127", {route["finding"] for route in result["existing_findings"]["routes"]})

    def test_accepts_terminal_coverage_rows_after_routed_batch_completion(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            findings = root / "findings"
            findings.mkdir()
            for finding_id in FINDING_ROUTES:
                (findings / f"{finding_id}.md").write_text("## 待验证\n", encoding="utf-8")
            coverage = root / "coverage.md"
            coverage.write_text("".join(
                f"| `{claim_id}` | source | topic | `completed` | `completed:{batch}` | closed |\n"
                for claim_id, batch in SOURCE_CLAIM_ROUTES.items()
            ), encoding="utf-8")
            queue = root / "queue.json"
            queue.write_text(json.dumps({
                "candidate_count": 3,
                "queue_sha256": "A" * 64,
                "disposition_counts": {"baseline": 1, "out-of-scope": 1},
                "final_round_routing": {"candidate_count": 1, "batch_counts": {"EVT-02": 1}, "unrouted_count": 0},
            }), encoding="utf-8")
            result = build_status(findings, coverage, queue)
        self.assertTrue(result["validation"]["valid"])
        self.assertEqual(result["existing_findings"]["routes"][0]["batch"], "SCN-01")


if __name__ == "__main__":
    unittest.main()
