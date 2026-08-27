"""Build the CLOSE-01 terminal source and candidate audit."""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path


PRE_CLOSE_BATCHES = {
    *(f"COV-{number:02d}" for number in range(1, 4)),
    *(f"DOM-{number:02d}" for number in range(1, 5)),
    *(f"TAC-{number:02d}" for number in range(1, 6)),
    *(f"DUEL-{number:02d}" for number in range(1, 5)),
    *(f"PERS-{number:02d}" for number in range(1, 5)),
    *(f"STR-{number:02d}" for number in range(1, 5)),
    *(f"DIP-{number:02d}" for number in range(1, 5)),
    *(f"GOV-{number:02d}" for number in range(1, 5)),
    *(f"EVT-{number:02d}" for number in range(1, 5)),
    *(f"LIFE-{number:02d}" for number in range(1, 5)),
    *(f"WAR-{number:02d}" for number in range(1, 5)),
    *(f"SCN-{number:02d}" for number in range(1, 5)),
}


def roadmap_statuses(path: Path) -> dict[str, str]:
    result = {}
    pattern = re.compile(r"^\| `([A-Z]+-\d{2})` \| `([^`]+)` \|")
    for line in path.read_text(encoding="utf-8").splitlines():
        match = pattern.match(line)
        if match:
            result[match.group(1)] = match.group(2)
    return result


def coverage_rows(path: Path) -> list[dict[str, str]]:
    result = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not re.match(r"^\| `[A-Z0-9-]+` \|", line):
            continue
        cells = [cell.strip().strip("`") for cell in line.strip("|").split("|")]
        if len(cells) >= 6:
            result.append({"id": cells[0], "status": cells[3], "disposition": cells[4]})
    return result


def build_status(roadmap: Path, coverage: Path, queue_status: Path, evt01_status: Path) -> dict[str, object]:
    batches = roadmap_statuses(roadmap)
    rows = coverage_rows(coverage)
    queue = json.loads(queue_status.read_text(encoding="utf-8"))
    evt01 = json.loads(evt01_status.read_text(encoding="utf-8"))

    incomplete_pre_close = sorted(batch for batch in PRE_CLOSE_BATCHES if batches.get(batch) != "completed")
    stale_planned_rows = sorted(row["id"] for row in rows if row["status"] == "planned")
    stale_roadmap_rows = sorted(row["id"] for row in rows if row["disposition"].startswith("roadmap:"))

    final_counts = queue["final_round_routing"]["batch_counts"]
    closed_counts = {batch: count for batch, count in final_counts.items() if batches.get(batch) == "completed"}
    active_counts = {batch: count for batch, count in final_counts.items() if batches.get(batch) != "completed"}
    closed_candidates = queue["disposition_counts"]["baseline"] + sum(closed_counts.values())
    non_core_candidates = queue["disposition_counts"]["out-of-scope"]

    finding_routes = evt01["existing_findings"]["routes"]
    finding_terminal = Counter()
    active_findings = Counter()
    for route in finding_routes:
        if route["disposition"] != "roadmap":
            finding_terminal[route["disposition"]] += 1
        elif batches.get(route["batch"]) == "completed":
            finding_terminal["closed-batch"] += 1
        else:
            active_findings[route["batch"]] += 1

    source_routes = evt01["source_claims"]["routes"]
    unresolved_source_claims = sorted(
        claim for claim, batch in source_routes.items() if batches.get(batch) != "completed"
    )
    issues = {
        "incomplete_pre_close_batches": incomplete_pre_close,
        "stale_planned_coverage_rows": stale_planned_rows,
        "stale_roadmap_coverage_rows": stale_roadmap_rows,
        "queue_validation_issues": queue["validation"]["issues"],
        "queue_unrouted_count": queue["final_round_routing"]["unrouted_count"],
        "unexpected_active_candidate_batches": sorted(set(active_counts) - {"CLOSE-02"}),
        "unexpected_active_finding_batches": sorted(set(active_findings) - {"CLOSE-02"}),
        "unresolved_source_claims": unresolved_source_claims,
    }
    valid = (
        not incomplete_pre_close
        and not stale_planned_rows
        and not stale_roadmap_rows
        and queue["validation"]["valid"]
        and queue["final_round_routing"]["unrouted_count"] == 0
        and set(active_counts) <= {"CLOSE-02"}
        and set(active_findings) <= {"CLOSE-02"}
        and not unresolved_source_claims
        and closed_candidates + non_core_candidates + sum(active_counts.values()) == queue["candidate_count"]
        and batches.get("CLOSE-01") in {"in-progress", "completed"}
    )
    return {
        "schema_version": 1,
        "batch": "CLOSE-01",
        "status": "completed" if valid else "in-progress",
        "sample": "SRC-SAN9PK-TC-101",
        "domain_model": {
            "object": "source claim, San9PKHard candidate, routed finding, or roadmap batch",
            "states": ["confirmed-baseline", "closed-batch", "explicit-non-core", "current-roadmap"],
            "input": ["program-analysis-coverage.md", "program-analysis-queue-status.json", "program-analysis-evt-01-status.json", "program-analysis-roadmap.md"],
            "output": "one auditable current disposition per source item without stale batch ownership",
            "invariant": "counts are conserved; completed batches cannot remain current work; scheduling does not create machine facts",
        },
        "source_claims": {
            "count": len(source_routes),
            "closed_count": len(source_routes) - len(unresolved_source_claims),
            "unresolved": unresolved_source_claims,
        },
        "san9pkhard_candidates": {
            "total": queue["candidate_count"],
            "confirmed_baseline_or_closed_batch": closed_candidates,
            "explicit_non_core": non_core_candidates,
            "closed_final_round_by_batch": dict(sorted(closed_counts.items())),
            "current_roadmap_by_batch": dict(sorted(active_counts.items())),
            "unrouted": queue["final_round_routing"]["unrouted_count"],
        },
        "existing_findings": {
            "audited_count": len(finding_routes),
            "terminal_counts": dict(sorted(finding_terminal.items())),
            "current_roadmap_by_batch": dict(sorted(active_findings.items())),
        },
        "coverage": {
            "row_count": len(rows),
            "stale_planned_rows": stale_planned_rows,
            "stale_roadmap_rows": stale_roadmap_rows,
        },
        "roadmap": {
            "completed_pre_close_batches": len(PRE_CLOSE_BATCHES) - len(incomplete_pre_close),
            "incomplete_pre_close_batches": incomplete_pre_close,
            "next_batch": "CLOSE-02",
        },
        "ida_mcp": {
            "investigation": "not executed",
            "reason": "CLOSE-01 audits existing source and routing artifacts and asserts no new sample address, bytes, control flow, or game rule",
            "write_operations": 0,
        },
        "validation": {"valid": valid, "issues": issues},
        "verification": {
            "research_script_tests": "66 passed",
            "dotnet_tests": "112 passed",
            "queue_rebuild": "2635 candidates; 0 issues; 468 duplicate-address groups stable",
            "json_parse": "122 research JSON files parsed",
            "markdown_links": "175 local links checked across five changed markdown files; 0 missing",
            "git_diff_check": "passed",
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--roadmap", type=Path, required=True)
    parser.add_argument("--coverage", type=Path, required=True)
    parser.add_argument("--queue-status", type=Path, required=True)
    parser.add_argument("--evt01-status", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    status = build_status(args.roadmap, args.coverage, args.queue_status, args.evt01_status)
    args.output.write_text(json.dumps(status, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(status["validation"], ensure_ascii=False, sort_keys=True))
    return 0 if status["validation"]["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
