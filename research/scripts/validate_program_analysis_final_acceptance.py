"""Validate the R0-R12 program-analysis closure from versioned evidence."""

from __future__ import annotations

import argparse
import csv
import json
import re
import subprocess
from collections import Counter
from pathlib import Path
from urllib.parse import unquote


EXPECTED_BATCHES = (
    {f"COV-{index:02d}" for index in range(1, 4)}
    | {f"DOM-{index:02d}" for index in range(1, 5)}
    | {f"TAC-{index:02d}" for index in range(1, 6)}
    | {f"DUEL-{index:02d}" for index in range(1, 5)}
    | {f"PERS-{index:02d}" for index in range(1, 5)}
    | {
        f"{prefix}-{index:02d}"
        for prefix in ("STR", "DIP", "GOV", "EVT", "LIFE", "WAR", "SCN", "CLOSE")
        for index in range(1, 5)
    }
)
FINAL_STATUS_NAMES = [
    *(f"evt-{index:02d}" for index in range(1, 5)),
    *(f"life-{index:02d}" for index in range(1, 5)),
    *(f"war-{index:02d}" for index in range(1, 5)),
    *(f"scn-{index:02d}" for index in range(1, 5)),
    "close-01", "close-02", "close-03",
]
MACHINE_STATUS_RECORDS = {
    "evt-02": "ida_writes",
    "evt-03": "ida_writes",
    "evt-04": "ida",
    "life-01": "ida_writes",
    "life-02": "ida_writes",
    "life-03": "ida_writes",
    "war-01": "ida_writes",
    "war-02": "ida_writes",
    "war-03": "ida_writes",
    "war-04": "ida_writes",
    "scn-01": "ida_writes",
    "scn-02": "ida_writes",
    "scn-03": "ida_writes",
    "scn-04": "ida_writes",
    "close-02": "ida_mcp",
}
FORBIDDEN_TRACKED_PATTERNS = (
    re.compile(r"(?i)\.(?:i64|idb|id0|id1|id2|nam|til|exe|dll|s9)$"),
    re.compile(r"(?i)^(?:private/(?!README\.md$)|artifacts/|local/generated/)"),
)
EXPECTED_SAMPLE_HASH = "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
HISTORICAL_FINDINGS = {f"RE-SAN9PK-{index:04d}" for index in range(1, 148)}
HISTORICAL_SYMBOL_COUNT = 419


def completed_disposition(value: str, batches: dict[str, str]) -> bool:
    match = re.fullmatch(r"completed:([A-Z]+-\d{2}(?:\+[A-Z]+-\d{2})*)", value)
    return match is not None and all(batches.get(batch) == "completed" for batch in match.group(1).split("+"))


def load_json(path: Path) -> object:
    return json.loads(path.read_text(encoding="utf-8"))


def strip_code(value: str) -> str:
    value = value.strip()
    return value[1:-1] if len(value) >= 2 and value.startswith("`") and value.endswith("`") else value


def roadmap_batches(text: str) -> dict[str, str]:
    result: dict[str, str] = {}
    for match in re.finditer(r"(?m)^\| `([A-Z]+-\d{2})` \| `([^`]+)` \|", text):
        batch, status = match.groups()
        if batch in result:
            raise ValueError(f"duplicate roadmap batch: {batch}")
        result[batch] = status
    return result


def markdown_link_audit(paths: list[Path]) -> tuple[int, list[dict[str, str]]]:
    pattern = re.compile(r"\[[^\]]+\]\(([^)#]+)(?:#[^)]+)?\)")
    count = 0
    missing: list[dict[str, str]] = []
    for path in paths:
        text = path.read_text(encoding="utf-8")
        for match in pattern.finditer(text):
            target = match.group(1)
            if re.match(r"^(?:https?://|mailto:|data:)", target):
                continue
            count += 1
            resolved = (path.parent / unquote(target)).resolve()
            if not resolved.exists():
                missing.append({"file": path.as_posix(), "target": target})
    return count, missing


def write_record_is_verified(record: dict[str, object]) -> tuple[bool, list[str]]:
    problems: list[str] = []
    saved = record.get("saved") is True or record.get("save") == "succeeded"
    readback = record.get("readback_verified") is True or "verified" in str(record.get("read_back", "")).lower()
    if not saved:
        problems.append("not saved")
    if not readback:
        problems.append("readback not verified")
    if record.get("sample_bytes_modified") not in (None, False):
        problems.append("sample bytes modified")
    if record.get("program_byte_changes") not in (None, 0):
        problems.append("program bytes changed")
    return not problems, problems


def validate_repository(root: Path) -> dict[str, object]:
    issues: dict[str, list[object]] = {}

    def issue(kind: str, value: object) -> None:
        issues.setdefault(kind, []).append(value)

    research = root / "research"
    program_archive = research / "archive" / "program-analysis"
    program_status_archive = program_archive / "status"
    specs = root / "specs"
    roadmap_path = program_archive / "program-analysis-roadmap.md"
    roadmap_text = roadmap_path.read_text(encoding="utf-8")
    try:
        batches = roadmap_batches(roadmap_text)
    except ValueError as error:
        batches = {}
        issue("roadmap_parse", str(error))
    if set(batches) != EXPECTED_BATCHES:
        issue("roadmap_batch_set", {
            "missing": sorted(EXPECTED_BATCHES - set(batches)),
            "extra": sorted(set(batches) - EXPECTED_BATCHES),
        })
    incomplete = {batch: status for batch, status in batches.items() if status != "completed"}
    if incomplete:
        issue("incomplete_roadmap_batches", incomplete)
    if "- 状态：已完成" not in roadmap_text:
        issue("roadmap_header", "roadmap is not marked completed")

    r0_r3 = (program_archive / "program-analysis-r0-r3-acceptance.md").read_text(encoding="utf-8")
    if "- 验收结论：通过" not in r0_r3:
        issue("r0_r3_acceptance", "not passed")
    r4_r7 = load_json(program_status_archive / "program-analysis-r4-r7-acceptance.json")
    expected_r4_r7 = {
        f"{prefix}-{index:02d}"
        for prefix in ("PERS", "STR", "DIP", "GOV")
        for index in range(1, 5)
    }
    if r4_r7.get("status") != "completed" or set(r4_r7.get("completed_batches", [])) != expected_r4_r7:
        issue("r4_r7_acceptance", r4_r7.get("completed_batches"))

    final_statuses: dict[str, dict[str, object]] = {}
    for name in FINAL_STATUS_NAMES:
        path = program_status_archive / f"program-analysis-{name}-status.json"
        if not path.is_file():
            issue("missing_final_status", name)
            continue
        value = load_json(path)
        final_statuses[name] = value
        if value.get("status") != "completed":
            issue("incomplete_final_status", {"name": name, "status": value.get("status")})
    for name, key in MACHINE_STATUS_RECORDS.items():
        status = final_statuses.get(name, {})
        record = status.get(key)
        if not isinstance(record, dict):
            issue("missing_ida_write_record", {"name": name, "key": key})
            continue
        valid, problems = write_record_is_verified(record)
        if not valid:
            issue("unverified_ida_write", {"name": name, "problems": problems})
    evt01_ida = final_statuses.get("evt-01", {}).get("ida_mcp", {})
    close01_ida = final_statuses.get("close-01", {}).get("ida_mcp", {})
    life04_ida = final_statuses.get("life-04", {}).get("ida_writes", {})
    if evt01_ida.get("write_operations") != 0 or close01_ida.get("write_operations") != 0:
        issue("source_audit_write", "EVT-01 or CLOSE-01 recorded a write")
    if life04_ida.get("required") is not False:
        issue("life04_ida_boundary", life04_ida)

    queue = load_json(program_status_archive / "program-analysis-queue-status.json")
    if queue.get("candidate_count") != 2635 or queue.get("expected_candidate_count") != 2635:
        issue("queue_count", {"actual": queue.get("candidate_count"), "expected": queue.get("expected_candidate_count")})
    if not queue.get("validation", {}).get("valid"):
        issue("queue_validation", queue.get("validation"))
    final_routing = queue.get("final_round_routing", {})
    if final_routing.get("unrouted_count") != 0:
        issue("queue_unrouted", final_routing.get("unrouted_count"))
    for batch in final_routing.get("batch_counts", {}):
        if batches.get(batch) != "completed":
            issue("queue_batch_not_completed", batch)
    queue_counts = queue.get("disposition_counts", {})
    if sum(queue_counts.values()) != 2635:
        issue("queue_conservation", queue_counts)

    evt = load_json(program_status_archive / "program-analysis-evt-01-status.json")
    if evt.get("source_claims", {}).get("atomic_final_claim_count") != 10:
        issue("source_claim_count", evt.get("source_claims"))
    existing = evt.get("existing_findings", {})
    if existing.get("pending_marker_finding_count") != 99:
        issue("pending_finding_count", existing.get("pending_marker_finding_count"))
    for route in existing.get("routes", []):
        if route.get("disposition") == "roadmap" and batches.get(route.get("batch")) != "completed":
            issue("pending_finding_open_route", route)
    for batch in evt.get("source_claims", {}).get("routes", {}).values():
        if batches.get(batch) != "completed":
            issue("source_claim_open_route", batch)

    coverage_rows: list[dict[str, str]] = []
    for line in (program_archive / "program-analysis-coverage.md").read_text(encoding="utf-8").splitlines():
        if not line.startswith("| `"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) < 6:
            issue("coverage_row_shape", line)
            continue
        coverage_rows.append({"id": strip_code(cells[0]), "status": strip_code(cells[3]), "disposition": strip_code(cells[4])})
    if len(coverage_rows) != 83:
        issue("coverage_count", len(coverage_rows))
    if len({row["id"] for row in coverage_rows}) != len(coverage_rows):
        issue("coverage_duplicate_id", "duplicate IDs")
    allowed_statuses = {"confirmed", "inferred", "conflict", "completed"}
    for row in coverage_rows:
        if row["status"] not in allowed_statuses:
            issue("coverage_nonterminal_status", row)
        disposition = row["disposition"]
        if disposition in {"baseline", "duplicate", "out-of-scope"}:
            continue
        if not completed_disposition(disposition, batches):
            issue("coverage_nonterminal_disposition", row)

    formulas = load_json(program_status_archive / "formula-status.json")
    if len(formulas) != 30:
        issue("formula_count", len(formulas))
    for formula in formulas:
        if formula.get("status") != "original-confirmed" or formula.get("missing_evidence"):
            issue("formula_not_closed", formula.get("claim_id"))
    if "当前队列为空" not in (program_archive / "formula-experiment-queue.md").read_text(encoding="utf-8"):
        issue("formula_experiment_queue", "not empty")

    close03 = load_json(program_status_archive / "program-analysis-close-03-status.json")
    if close03.get("status") != "completed" or not close03.get("validation", {}).get("valid"):
        issue("behavior_contract", close03.get("validation"))
    if close03.get("behavior_spec_count") != 44 or close03.get("research_detail_leak_count") != 0:
        issue("behavior_contract_counts", {"specs": close03.get("behavior_spec_count"), "leaks": close03.get("research_detail_leak_count")})

    finding_paths = sorted(path for path in (research / "findings").glob("RE-SAN9PK-*.md")
                           if re.fullmatch(r"RE-SAN9PK-\d{4}", path.stem))
    finding_ids = {path.stem for path in finding_paths}
    index_text = (research / "findings" / "README.md").read_text(encoding="utf-8")
    indexed_ids = set(re.findall(r"RE-SAN9PK-\d{4}(?=\.md)", index_text))
    if finding_ids != indexed_ids:
        issue("finding_index", {"unindexed": sorted(finding_ids - indexed_ids), "missing": sorted(indexed_ids - finding_ids)})
    if missing_baseline := sorted(HISTORICAL_FINDINGS - finding_ids):
        issue("missing_historical_findings", missing_baseline)

    symbol_path = research / "symbols" / "san9pk-tc-101.csv"
    with symbol_path.open(encoding="utf-8-sig", newline="") as stream:
        symbols = list(csv.DictReader(stream))
    if len(symbols) < HISTORICAL_SYMBOL_COUNT:
        issue("symbol_count", len(symbols))
    for field in ("address", "symbol"):
        duplicates = sorted(key for key, count in Counter(row[field] for row in symbols).items() if count > 1)
        if duplicates:
            issue(f"duplicate_symbol_{field}", duplicates)
    for row in symbols:
        if not re.fullmatch(r"0x[0-9A-Fa-f]{8}", row["address"]):
            issue("invalid_symbol_address", row["address"])
        for evidence_id in row["evidence"].split("+"):
            if evidence_id and evidence_id not in finding_ids:
                issue("missing_symbol_evidence", {"symbol": row["symbol"], "evidence": evidence_id})
    type_path = research / "symbols" / "san9pk-tc-101-types.csv"
    with type_path.open(encoding="utf-8-sig", newline="") as stream:
        types = list(csv.DictReader(stream))
    if len(types) != 8:
        issue("type_summary_count", len(types))

    ida = load_json(program_status_archive / "program-analysis-final-ida-readback.json")
    if ida.get("sample", {}).get("sha256") != EXPECTED_SAMPLE_HASH:
        issue("final_ida_sample", ida.get("sample"))
    ida_state = ida.get("ida_mcp", {})
    if ida_state.get("health") != "ok" or not ida_state.get("auto_analysis_ready") or not ida_state.get("hexrays_ready"):
        issue("final_ida_health", ida_state)
    if ida_state.get("write_operations") != 0 or ida_state.get("program_byte_changes") != 0:
        issue("final_ida_write", ida_state)
    symbol_readback = ida.get("symbol_readback", {})
    # The archived readback proves the 2026-08-27 baseline, not subsequent IDA edits or additions.
    if symbol_readback.get("row_count") != HISTORICAL_SYMBOL_COUNT or symbol_readback.get("final_name_mismatch_count") != 0 or symbol_readback.get("function_entry_mismatch_count") != 0:
        issue("final_symbol_readback", symbol_readback)
    type_readback = ida.get("type_readback", {})
    if type_readback.get("row_count") != len(types) or type_readback.get("size_mismatch_count") != 0 or type_readback.get("missing_accessor_count") != 0:
        issue("final_type_readback", type_readback)

    non_core = load_json(program_status_archive / "program-analysis-non-core-archive.json")
    hard = non_core.get("san9pkhard_candidates", {})
    old = non_core.get("existing_findings", {})
    claims = non_core.get("source_claims", {})
    if hard.get("count") != 832 or sum(hard.get("subsystems", {}).values()) != 832:
        issue("non_core_hard", hard)
    evt_out = {route["finding"] for route in existing.get("routes", []) if route.get("disposition") == "out-of-scope"}
    if old.get("count") != 29 or set(old.get("ids", [])) != evt_out:
        issue("non_core_findings", {"archive": old, "expected": sorted(evt_out)})
    coverage_out = {row["id"] for row in coverage_rows if row["disposition"] == "out-of-scope"}
    if claims.get("count") != 8 or set(claims.get("ids", [])) != coverage_out:
        issue("non_core_claims", {"archive": claims, "expected": sorted(coverage_out)})
    allowed_categories = {"content-entry", "presentation-ui", "tooling", "optional-deep-dive", "implementation-triggered-evidence"}
    actual_categories = {item.get("id") for item in non_core.get("remaining_categories", [])}
    if actual_categories != allowed_categories or non_core.get("core_required_experiments") or non_core.get("global_reverse_rounds_allowed") is not False:
        issue("non_core_boundary", non_core)
    if any(item.get("blocks_core_spec") is not False for item in non_core.get("optional_experiments", [])):
        issue("blocking_optional_experiment", non_core.get("optional_experiments"))

    markdown_paths = sorted([*research.rglob("*.md"), *specs.rglob("*.md")])
    link_count, missing_links = markdown_link_audit(markdown_paths)
    if missing_links:
        issue("missing_markdown_links", missing_links)
    json_paths = sorted([*research.rglob("*.json"), *specs.rglob("*.json")])
    json_paths = [path for path in json_paths if path.name != "program-analysis-close-04-status.json"]
    json_failures = []
    for path in json_paths:
        try:
            load_json(path)
        except (json.JSONDecodeError, UnicodeDecodeError) as error:
            json_failures.append({"path": path.as_posix(), "error": str(error)})
    if json_failures:
        issue("json_parse", json_failures)

    tracked = subprocess.run(
        ["git", "ls-files", "-z"], cwd=root, check=True, capture_output=True
    ).stdout.decode("utf-8").split("\0")
    forbidden = sorted(path for path in tracked if path and any(pattern.search(path) for pattern in FORBIDDEN_TRACKED_PATTERNS))
    if forbidden:
        issue("forbidden_tracked_files", forbidden)

    core_closed_candidates = queue_counts.get("baseline", 0) + queue_counts.get("roadmap", 0)
    return {
        "schema_version": 1,
        "batch": "CLOSE-04",
        "status": "completed" if not issues else "invalid",
        "sample": "SRC-SAN9PK-TC-101",
        "finding": "RE-SAN9PK-0147",
        "acceptance": "research/archive/program-analysis/program-analysis-r0-r12-acceptance.md",
        "roadmap": {"batch_count": len(batches), "completed_count": sum(status == "completed" for status in batches.values()), "incomplete": incomplete},
        "source_and_candidate_closure": {
            "coverage_rows": len(coverage_rows),
            "san9pkhard_candidates": queue.get("candidate_count"),
            "confirmed_baseline_or_closed_batch": core_closed_candidates,
            "explicit_non_core": queue_counts.get("out-of-scope"),
            "source_claims": evt.get("source_claims", {}).get("atomic_final_claim_count"),
            "routed_old_findings": existing.get("pending_marker_finding_count"),
            "unrouted": final_routing.get("unrouted_count"),
        },
        "evidence_indexes": {"findings": len(finding_ids), "symbols": len(symbols), "types": len(types), "behavior_specs": close03.get("behavior_spec_count")},
        "ida": {
            "historical_machine_batches_verified": len(MACHINE_STATUS_RECORDS),
            "final_symbol_readback": f"{symbol_readback.get('row_count')}/{HISTORICAL_SYMBOL_COUNT}",
            "final_type_readback": f"{type_readback.get('row_count')}/{len(types)}",
            "final_write_operations": ida_state.get("write_operations"),
        },
        "non_core_archive": {"hard_candidates": hard.get("count"), "old_findings": old.get("count"), "source_claims": claims.get("count"), "core_required_experiments": len(non_core.get("core_required_experiments", []))},
        "repository_audit": {"markdown_files": len(markdown_paths), "local_links": link_count, "json_inputs": len(json_paths), "forbidden_tracked_files": len(forbidden)},
        "validation": {"valid": not issues, "issue_counts": {key: len(values) for key, values in sorted(issues.items())}, "issues": dict(sorted(issues.items()))},
        "future_research_policy": "targeted-only: implementation/specification failure, contradictory new evidence, or explicit selection of an archived item",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = validate_repository(args.repo_root.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0 if result["validation"]["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
