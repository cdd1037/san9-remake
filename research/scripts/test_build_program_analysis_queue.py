import tempfile
import unittest
from pathlib import Path

from build_program_analysis_queue import build_queue, build_queue_status, classify_subsystem, route_candidate, validate_queue
from build_san9pkhard_catalog import build_catalog


class BuildProgramAnalysisQueueTests(unittest.TestCase):
    def make_root(self, directory: str) -> Path:
        root = Path(directory)
        (root / "sample.h").write_text(
            """
BYTE codeDraftThunk[] = {0x90};
BYTE codeDraftOldThunk[] = {0x75};
ASM_PATCH patchDraftThunk[] = {
  {0x401000, sizeof(codeDraftThunk), codeDraftThunk},
  {0x401000, sizeof(codeDraftOldThunk), codeDraftOldThunk}};
DWORD dwAddrKillerInterval[] = {0x402000};
""",
            encoding="utf-8",
        )
        (root / "sample.cpp").write_text(
            """
Thing::Thing() : m_dwDuelDeadAddr(0x403000) {}
void f() {
  DDX_Check(pDX, IDC_DRAFT, m_bDraft);
  WriteProcessMemory(h, (LPVOID)m_dwDuelDeadAddr, &value, BYTE_SIZE, 0);
}
""",
            encoding="utf-8",
        )
        for name in ("San9PKHardPara.txt", "San9PKHardPerson.txt", "San9PKHardCity.txt", "San9PKHardArray.txt", "San9PKHardSkill.txt"):
            (root / name).write_text("1\n", encoding="utf-8")
        return root

    def test_routes_every_active_observation_once(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            catalog = build_catalog(root)
            queue = build_queue(catalog, root)
            validation = validate_queue(queue, root)
        self.assertTrue(validation["valid"])
        self.assertEqual(len(queue["candidates"]), queue["expected_candidate_count"])
        routes = {item["name"]: item.get("batch") for item in queue["candidates"]}
        self.assertIsNone(routes["patchDraftThunk"])
        self.assertIsNone(routes["dwAddrKillerInterval[0]"])
        self.assertIsNone(routes["m_dwDuelDeadAddr"])

    def test_validation_rejects_invalid_status_address_source_and_omission(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            queue = build_queue(build_catalog(root), root)
            queue["expected_candidate_count"] += 1
            queue["candidates"][0]["status"] = "unknown"
            queue["candidates"][0]["addresses"] = ["401000"]
            queue["candidates"][0]["source_refs"] = [{"file": "missing.h", "line": 1}]
            validation = validate_queue(queue, root)
        self.assertFalse(validation["valid"])
        self.assertEqual(validation["issue_counts"]["silent_omission"], 1)
        self.assertEqual(validation["issue_counts"]["invalid_status"], 1)
        self.assertEqual(validation["issue_counts"]["invalid_address"], 1)
        self.assertEqual(validation["issue_counts"]["missing_source_file"], 1)

    def test_status_is_deterministic_for_same_queue(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            queue = build_queue(build_catalog(root), root)
            validation = validate_queue(queue, root)
            first = build_queue_status(queue, validation)
            second = build_queue_status(queue, validation)
        self.assertEqual(first, second)
        self.assertEqual(len(first["queue_sha256"]), 64)

    def test_uses_token_boundaries_and_does_not_bleed_from_next_line(self):
        self.assertEqual(classify_subsystem("m_bSubCounsellorJudge"), ("personnel-diplomacy", "counsellor"))
        self.assertEqual(classify_subsystem("restrainTarget"), ("tactical-ai", "target"))
        self.assertEqual(classify_subsystem("m_bCityStone"), ("infrastructure", "fallback-no-domain-keyword"))

    def test_closes_generic_tactic_candidate_to_baseline(self):
        status, disposition, batch, reason = route_candidate("tactics", "patchSecondKillerThunk")
        self.assertEqual((status, disposition, batch), ("completed", "baseline", None))
        self.assertEqual(reason, "tactic-coverage-closed-by-r2")

    def test_closes_personnel_diplomacy_candidates_to_baseline(self):
        cases = (
            "dwAddrSearchRange", "dwAddrInviteNewPerson", "m_dwAddrPersonFriendship",
            "m_dwOfficialLoyaltyAddr", "m_dwLoyatyAddr", "dwAddrGossipToPlayer",
            "dwAddrCounselorAdvise", "dwAddrLeagueSucceed",
        )
        for text in cases:
            with self.subTest(text=text):
                status, disposition, batch, _ = route_candidate("personnel-diplomacy", text)
                self.assertEqual((status, disposition, batch), ("completed", "baseline", None))

    def test_routes_final_round_strong_terms_before_historical_subsystems(self):
        cases = {
            ("infrastructure", "patchTransportTask"): "WAR-02",
            ("tactical-ai", "m_uMaxDurability"): "WAR-03",
            ("duel", "m_bAllPersonDead"): "WAR-04",
            ("personnel-diplomacy", "m_bDisableSearchEvent"): "EVT-03",
            ("events", "m_dwGameEndYearAddr"): "SCN-03",
        }
        for (subsystem, text), expected_batch in cases.items():
            with self.subTest(text=text):
                status, disposition, batch, _ = route_candidate(subsystem, text)
                self.assertEqual((status, disposition, batch), ("planned", "roadmap", expected_batch))

    def test_distinguishes_image_addresses_from_relative_offsets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            catalog = build_catalog(root)
            relative = catalog["sections"]["address_symbols"][0]
            relative["classification"] = "relative-offset-or-count"
            queue = build_queue(catalog, root)
            validation = validate_queue(queue, root)
        self.assertTrue(validation["valid"])
        self.assertIn("address_kind", queue["candidates"][0])


if __name__ == "__main__":
    unittest.main()
