import json
import tempfile
import unittest
from pathlib import Path

from validate_core_domain_contracts import validate_contracts


class ValidateCoreDomainContractsTests(unittest.TestCase):
    def make_fixture(self, root: Path) -> tuple[dict[str, object], Path, Path]:
        behavior_dir = root / "specs" / "behavior"
        decision_dir = root / "specs" / "decisions"
        source_dir = root / "src" / "Core"
        behavior_dir.mkdir(parents=True)
        decision_dir.mkdir(parents=True)
        source_dir.mkdir(parents=True)
        (source_dir / "Entry.cs").write_text("namespace Core;", encoding="utf-8")
        (behavior_dir / "BEH-SIMULATION-0001-test.md").write_text(
            "# test\n\n- 状态：已确认\n- 关联决策：[DEC-SIM-0001](../decisions/x.md)\n"
            "\n## 重制行为\n\n行为。\n\n## 状态与不变量\n\n不变量。\n"
            "\n## 验收标准\n\n- [ ] 验收。\n\n## 未决问题\n\n无。\n",
            encoding="utf-8",
        )
        (decision_dir / "DEC-SIM-0001-test.md").write_text(
            "# decision\n\n- 状态：已确认\n", encoding="utf-8"
        )
        contracts = {
            "schema_version": 1,
            "status": "confirmed",
            "domains": [{
                "id": "simulation",
                "classification": "core",
                "behavior_specs": ["BEH-SIMULATION-0001"],
                "objects": ["Clock"],
                "states": ["stable"],
                "inputs": ["command"],
                "outputs": ["snapshot"],
                "invariants": ["one clock"],
                "acceptance_gates": ["advance"],
                "depends_on": [],
                "implementation": {
                    "project": "Core",
                    "target_namespace": "Core",
                    "target_path": "src/Core",
                    "entry_types": ["Entry"],
                    "existing_anchors": ["src/Core/Entry.cs"],
                },
            }],
            "decision_gates": [{
                "id": "determinism",
                "kind": "system-redesign",
                "behavior_specs": ["BEH-SIMULATION-0001"],
                "decision": "DEC-SIM-0001",
                "disposition": "stable order",
            }],
            "unresolved_conflicts": [],
        }
        return contracts, behavior_dir, decision_dir

    def test_accepts_complete_contract(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            contracts, behavior_dir, decision_dir = self.make_fixture(root)
            result = validate_contracts(
                contracts, behavior_dir, decision_dir, root, {"simulation"}
            )
        self.assertTrue(result["validation"]["valid"])
        self.assertEqual(result["behavior_spec_count"], 1)
        self.assertEqual(result["acceptance_criteria_count"], 1)

    def test_rejects_omission_leak_missing_anchor_and_conflict(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            contracts, behavior_dir, decision_dir = self.make_fixture(root)
            extra = behavior_dir / "BEH-EVENT-0001-extra.md"
            extra.write_text("# extra\n\n- 状态：已确认\n", encoding="utf-8")
            domain = contracts["domains"][0]
            domain["objects"] = ["RE-SAMPLE at 0x401000"]
            domain["implementation"]["existing_anchors"] = ["src/Core/Missing.cs"]
            contracts["unresolved_conflicts"] = ["open"]
            result = validate_contracts(
                contracts, behavior_dir, decision_dir, root, {"simulation"}
            )
        issues = result["validation"]["issues"]
        self.assertFalse(result["validation"]["valid"])
        self.assertIn("unclassified_behavior_specs", issues)
        self.assertIn("research_detail_leak", issues)
        self.assertIn("missing_existing_anchor", issues)
        self.assertIn("unresolved_conflicts", issues)

    def test_rejects_dependency_cycle(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            contracts, behavior_dir, decision_dir = self.make_fixture(root)
            contracts["domains"].append({
                **json.loads(json.dumps(contracts["domains"][0])),
                "id": "event",
                "behavior_specs": [],
                "depends_on": ["simulation"],
            })
            contracts["domains"][0]["depends_on"] = ["event"]
            result = validate_contracts(
                contracts, behavior_dir, decision_dir, root, {"simulation", "event"}
            )
        self.assertIn("dependency_cycle", result["validation"]["issues"])


if __name__ == "__main__":
    unittest.main()
