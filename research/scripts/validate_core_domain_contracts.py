"""Validate the CLOSE-03 implementation-facing behavior contract graph."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from pathlib import Path


REQUIRED_CORE_DOMAINS = {
    "domestic-development",
    "ai",
    "simulation",
    "personnel",
    "person-lifecycle",
    "governance",
    "diplomacy",
    "strategy",
    "warfare",
    "duel",
    "event",
    "scenario",
}
REQUIRED_DOMAIN_FIELDS = {
    "objects",
    "states",
    "inputs",
    "outputs",
    "invariants",
    "acceptance_gates",
    "depends_on",
    "implementation",
}
REQUIRED_IMPLEMENTATION_FIELDS = {
    "project",
    "target_namespace",
    "target_path",
    "entry_types",
    "existing_anchors",
}
REQUIRED_BEHAVIOR_SECTIONS = {
    "重制行为",
    "状态与不变量",
    "验收标准",
    "未决问题",
}
LEAK_PATTERN = re.compile(r"(?:\bRE-[A-Z0-9-]+|(?i:\b0x[0-9a-f]+|\bsub_[0-9a-f]+|private/))")
BEHAVIOR_ID_PATTERN = re.compile(r"^(BEH-[A-Z-]+-\d{4})")
DECISION_ID_PATTERN = re.compile(r"^(DEC-[A-Z]+-\d{4})")


def document_id(path: Path, pattern: re.Pattern[str]) -> str | None:
    match = pattern.match(path.stem)
    return match.group(1) if match else None


def has_cycle(dependencies: dict[str, list[str]]) -> bool:
    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(node: str) -> bool:
        if node in visiting:
            return True
        if node in visited:
            return False
        visiting.add(node)
        if any(visit(dependency) for dependency in dependencies[node]):
            return True
        visiting.remove(node)
        visited.add(node)
        return False

    return any(visit(node) for node in dependencies)


def validate_contracts(
    contracts: dict[str, object],
    behavior_dir: Path,
    decision_dir: Path,
    repo_root: Path,
    required_core_domains: set[str] = REQUIRED_CORE_DOMAINS,
    contract_map: Path | None = None,
) -> dict[str, object]:
    issues: dict[str, list[object]] = {}

    def issue(kind: str, value: object) -> None:
        issues.setdefault(kind, []).append(value)

    behavior_paths = sorted(behavior_dir.glob("BEH-*.md"))
    behavior_by_id = {
        behavior_id: path
        for path in behavior_paths
        if (behavior_id := document_id(path, BEHAVIOR_ID_PATTERN))
    }
    decision_by_id = {
        decision_id: path
        for path in sorted(decision_dir.glob("DEC-*.md"))
        if (decision_id := document_id(path, DECISION_ID_PATTERN))
    }

    domains = contracts.get("domains", [])
    if not isinstance(domains, list):
        domains = []
        issue("invalid_domains", "domains must be a list")
    domain_ids: list[str] = []
    listed_specs: list[str] = []
    dependencies: dict[str, list[str]] = {}
    acceptance_by_domain: dict[str, int] = {}
    decisions_by_spec: dict[str, set[str]] = {}

    for domain in domains:
        if not isinstance(domain, dict):
            issue("invalid_domain", domain)
            continue
        domain_id = str(domain.get("id", ""))
        domain_ids.append(domain_id)
        classification = domain.get("classification")
        if classification not in {"core", "boundary"}:
            issue("invalid_classification", domain_id)
        missing_fields = sorted(REQUIRED_DOMAIN_FIELDS - domain.keys())
        if missing_fields:
            issue("missing_domain_fields", {"domain": domain_id, "fields": missing_fields})
        for field in REQUIRED_DOMAIN_FIELDS - {"depends_on", "implementation"}:
            if not isinstance(domain.get(field), list) or not domain.get(field):
                issue("empty_domain_contract", {"domain": domain_id, "field": field})
        specs = domain.get("behavior_specs", [])
        if not isinstance(specs, list) or not specs:
            issue("empty_behavior_specs", domain_id)
            specs = []
        listed_specs.extend(str(spec) for spec in specs)
        dependency_list = domain.get("depends_on", [])
        if not isinstance(dependency_list, list):
            issue("invalid_dependencies", domain_id)
            dependency_list = []
        dependencies[domain_id] = [str(value) for value in dependency_list]

        implementation = domain.get("implementation", {})
        if not isinstance(implementation, dict):
            issue("invalid_implementation", domain_id)
            implementation = {}
        missing_implementation = sorted(REQUIRED_IMPLEMENTATION_FIELDS - implementation.keys())
        if missing_implementation:
            issue("missing_implementation_fields", {"domain": domain_id, "fields": missing_implementation})
        target_path = str(implementation.get("target_path", ""))
        if not target_path.startswith("src/"):
            issue("invalid_target_path", {"domain": domain_id, "path": target_path})
        for anchor in implementation.get("existing_anchors", []):
            if not (repo_root / str(anchor)).is_file():
                issue("missing_existing_anchor", {"domain": domain_id, "path": anchor})

        acceptance_count = 0
        for spec_id in specs:
            path = behavior_by_id.get(str(spec_id))
            if path is None:
                issue("missing_behavior_spec", {"domain": domain_id, "spec": spec_id})
                continue
            text = path.read_text(encoding="utf-8")
            if not re.search(r"(?m)^- 状态：已确认", text):
                issue("unconfirmed_behavior_spec", spec_id)
            if classification == "core" and not re.search(r"(?m)^- 关联决策：", text):
                issue("missing_decision_metadata", spec_id)
            decision_line = re.search(r"(?m)^- 关联决策：(.+)$", text)
            linked_decisions = set(re.findall(r"DEC-[A-Z]+-\d{4}", decision_line.group(1))) if decision_line else set()
            decisions_by_spec[str(spec_id)] = linked_decisions
            for decision_id in linked_decisions:
                decision_path = decision_by_id.get(decision_id)
                if decision_path is None:
                    issue("behavior_missing_decision", {"spec": spec_id, "decision": decision_id})
                elif not re.search(r"(?m)^- 状态：已确认", decision_path.read_text(encoding="utf-8")):
                    issue("behavior_unconfirmed_decision", {"spec": spec_id, "decision": decision_id})
            headings = set(re.findall(r"(?m)^## (.+)$", text))
            missing_sections = sorted(REQUIRED_BEHAVIOR_SECTIONS - headings)
            if missing_sections:
                issue("missing_behavior_sections", {"spec": spec_id, "sections": missing_sections})
            acceptance_count += len(re.findall(r"(?m)^- \[[ xX]\] ", text))
        acceptance_by_domain[domain_id] = acceptance_count
        if acceptance_count == 0:
            issue("missing_acceptance_criteria", domain_id)

    duplicate_domains = sorted(key for key, count in Counter(domain_ids).items() if count > 1)
    duplicate_specs = sorted(key for key, count in Counter(listed_specs).items() if count > 1)
    if duplicate_domains:
        issue("duplicate_domains", duplicate_domains)
    if duplicate_specs:
        issue("duplicate_behavior_specs", duplicate_specs)
    missing_manifest_specs = sorted(set(behavior_by_id) - set(listed_specs))
    extra_manifest_specs = sorted(set(listed_specs) - set(behavior_by_id))
    if missing_manifest_specs:
        issue("unclassified_behavior_specs", missing_manifest_specs)
    if extra_manifest_specs:
        issue("unknown_behavior_specs", extra_manifest_specs)

    actual_core_domains = {
        str(domain.get("id"))
        for domain in domains
        if isinstance(domain, dict) and domain.get("classification") == "core"
    }
    if actual_core_domains != required_core_domains:
        issue("core_domain_set", {
            "missing": sorted(required_core_domains - actual_core_domains),
            "extra": sorted(actual_core_domains - required_core_domains),
        })
    known_domains = set(domain_ids)
    for domain_id, dependency_list in dependencies.items():
        for dependency in dependency_list:
            if dependency == domain_id:
                issue("self_dependency", domain_id)
            elif dependency not in known_domains:
                issue("unknown_dependency", {"domain": domain_id, "dependency": dependency})
    if dependencies and not any(
        key in issues for key in ("duplicate_domains", "unknown_dependency", "self_dependency")
    ) and has_cycle(dependencies):
        issue("dependency_cycle", dependencies)

    decision_gates = contracts.get("decision_gates", [])
    if not isinstance(decision_gates, list):
        decision_gates = []
        issue("invalid_decision_gates", "decision_gates must be a list")
    for gate in decision_gates:
        gate_id = str(gate.get("id", ""))
        decision = str(gate.get("decision", ""))
        if gate.get("kind") not in {"system-redesign", "intentional-difference", "original-bug", "parameter-adjustment", "source-conflict"}:
            issue("invalid_decision_gate_kind", gate_id)
        path = decision_by_id.get(decision)
        if path is None:
            issue("missing_decision", {"gate": gate_id, "decision": decision})
        elif not re.search(r"(?m)^- 状态：已确认", path.read_text(encoding="utf-8")):
            issue("unconfirmed_decision", decision)
        for spec_id in gate.get("behavior_specs", []):
            if spec_id not in behavior_by_id:
                issue("decision_gate_unknown_spec", {"gate": gate_id, "spec": spec_id})
            elif decision not in decisions_by_spec.get(str(spec_id), set()):
                issue("decision_gate_unlinked_spec", {"gate": gate_id, "spec": spec_id, "decision": decision})

    canonical = json.dumps(contracts, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    leak_text = canonical
    map_sha256 = None
    map_text = None
    if contract_map is not None:
        if not contract_map.is_file():
            issue("missing_contract_map", str(contract_map))
        else:
            map_text = contract_map.read_text(encoding="utf-8")
            leak_text += "\n" + map_text
            map_sha256 = hashlib.sha256(map_text.encode("utf-8")).hexdigest().upper()
            for domain_id in domain_ids:
                if f"`{domain_id}`" not in map_text:
                    issue("contract_map_missing_domain", domain_id)
            for gate in decision_gates:
                decision_id = str(gate.get("decision", ""))
                if f"`{decision_id}`" not in map_text:
                    issue("contract_map_missing_decision", decision_id)
    leak_matches = sorted(set(match.group(0) for match in LEAK_PATTERN.finditer(leak_text)))
    if leak_matches:
        issue("research_detail_leak", leak_matches)
    if contracts.get("unresolved_conflicts"):
        issue("unresolved_conflicts", contracts["unresolved_conflicts"])

    classifications = Counter(
        str(domain.get("classification")) for domain in domains if isinstance(domain, dict)
    )
    spec_classifications = Counter()
    for domain in domains:
        if isinstance(domain, dict):
            spec_classifications[str(domain.get("classification"))] += len(domain.get("behavior_specs", []))
    return {
        "schema_version": 1,
        "batch": "CLOSE-03",
        "status": "completed" if not issues else "invalid",
        "finding": "RE-SAN9PK-0146",
        "artifacts": [
            "specs/behavior/core-domain-contracts.json",
            "specs/behavior/core-domain-dependency-map.md",
        ],
        "contract_sha256": hashlib.sha256(canonical.encode("utf-8")).hexdigest().upper(),
        "contract_map_sha256": map_sha256,
        "domain_count": len(domains),
        "domain_classification_counts": dict(sorted(classifications.items())),
        "behavior_spec_count": len(behavior_by_id),
        "behavior_spec_classification_counts": dict(sorted(spec_classifications.items())),
        "acceptance_criteria_count": sum(acceptance_by_domain.values()),
        "acceptance_criteria_by_domain": dict(sorted(acceptance_by_domain.items())),
        "dependency_edge_count": sum(len(values) for values in dependencies.values()),
        "decision_gate_count": len(decision_gates),
        "implementation_entry_count": sum(
            len(domain.get("implementation", {}).get("entry_types", []))
            for domain in domains if isinstance(domain, dict)
        ),
        "research_detail_leak_count": len(leak_matches),
        "validation": {
            "valid": not issues,
            "issue_counts": {key: len(value) for key, value in sorted(issues.items())},
            "issues": dict(sorted(issues.items())),
        },
        "reproduce": "python research/scripts/validate_core_domain_contracts.py --contracts specs/behavior/core-domain-contracts.json --map specs/behavior/core-domain-dependency-map.md --behavior-dir specs/behavior --decision-dir specs/decisions --repo-root . --output research/program-analysis-close-03-status.json",
        "next_batch": "CLOSE-04" if not issues else None,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--contracts", type=Path, required=True)
    parser.add_argument("--behavior-dir", type=Path, required=True)
    parser.add_argument("--decision-dir", type=Path, required=True)
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--map", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    contracts = json.loads(args.contracts.read_text(encoding="utf-8"))
    result = validate_contracts(
        contracts, args.behavior_dir, args.decision_dir, args.repo_root,
        contract_map=args.map,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0 if result["validation"]["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
