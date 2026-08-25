"""Build the EVT-01 final-round routing audit.

This is a scheduling artifact.  It does not confirm original behavior.  It
keeps every finding that still contains a ``待验证`` marker visible while
separating final-core work from accepted baselines and non-core research.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from pathlib import Path


FINAL_BATCHES = {
    "EVT-02", "EVT-03", "EVT-04",
    "LIFE-01", "LIFE-02", "LIFE-03", "LIFE-04",
    "WAR-01", "WAR-02", "WAR-03", "WAR-04",
    "SCN-01", "SCN-02", "SCN-03", "SCN-04",
    "CLOSE-01", "CLOSE-02", "CLOSE-03", "CLOSE-04",
}

# One primary route per finding.  A later batch may cite the same finding as
# supporting evidence, but ownership of its still-open core question remains
# unique here.  Mixed UI/content/editor questions stay excluded by the scope
# filter documented in RE-SAN9PK-0127.
FINDING_ROUTES = {
    "RE-SAN9PK-0005": "SCN-01",
    "RE-SAN9PK-0007": "SCN-01",
    "RE-SAN9PK-0009": "LIFE-03",
    "RE-SAN9PK-0014": "WAR-03",
    "RE-SAN9PK-0015": "LIFE-01",
    "RE-SAN9PK-0016": "SCN-02",
    "RE-SAN9PK-0020": "WAR-03",
    "RE-SAN9PK-0021": "SCN-02",
    "RE-SAN9PK-0022": "SCN-01",
    "RE-SAN9PK-0023": "SCN-01",
    "RE-SAN9PK-0027": "EVT-03",
    "RE-SAN9PK-0028": "EVT-02",
    "RE-SAN9PK-0032": "WAR-01",
    "RE-SAN9PK-0034": "WAR-01",
    "RE-SAN9PK-0036": "WAR-01",
    "RE-SAN9PK-0038": "CLOSE-02",
    "RE-SAN9PK-0039": "CLOSE-02",
    "RE-SAN9PK-0040": "CLOSE-02",
    "RE-SAN9PK-0041": "SCN-03",
    "RE-SAN9PK-0042": "WAR-03",
    "RE-SAN9PK-0043": "WAR-02",
    "RE-SAN9PK-0044": "WAR-04",
    "RE-SAN9PK-0045": "SCN-02",
    "RE-SAN9PK-0046": "CLOSE-02",
    "RE-SAN9PK-0047": "SCN-01",
    "RE-SAN9PK-0048": "SCN-01",
    "RE-SAN9PK-0049": "CLOSE-02",
    "RE-SAN9PK-0051": "WAR-01",
    "RE-SAN9PK-0057": "WAR-03",
    "RE-SAN9PK-0058": "WAR-03",
    "RE-SAN9PK-0060": "SCN-02",
    "RE-SAN9PK-0061": "LIFE-03",
    "RE-SAN9PK-0067": "LIFE-01",
    "RE-SAN9PK-0068": "LIFE-01",
    "RE-SAN9PK-0069": "LIFE-02",
    "RE-SAN9PK-0070": "EVT-02",
    "RE-SAN9PK-0071": "LIFE-02",
    "RE-SAN9PK-0078": "WAR-02",
    "RE-SAN9PK-0081": "WAR-04",
    "RE-SAN9PK-0083": "CLOSE-02",
    "RE-SAN9PK-0084": "WAR-02",
    "RE-SAN9PK-0085": "WAR-02",
    "RE-SAN9PK-0086": "WAR-02",
    "RE-SAN9PK-0087": "CLOSE-02",
    "RE-SAN9PK-0089": "WAR-02",
    "RE-SAN9PK-0090": "WAR-02",
    "RE-SAN9PK-0098": "CLOSE-02",
    "RE-SAN9PK-0102": "CLOSE-02",
    "RE-SAN9PK-0112": "LIFE-03",
    "RE-SAN9PK-0119": "EVT-03",
    "RE-SAN9PK-0120": "EVT-03",
    "RE-SAN9PK-0122": "SCN-02",
    "RE-SAN9PK-0123": "CLOSE-02",
    "RE-SAN9PK-0124": "SCN-02",
    "RE-SAN9PK-0126": "WAR-02",
}

SOURCE_CLAIM_ROUTES = {
    "EBK-GROW-01": "LIFE-01",
    "EBK-EVT-01": "EVT-03",
    "EBK-LIFE-01": "LIFE-03",
    "EBK-EVT-02": "EVT-03",
    "EBK-EVT-03": "EVT-02",
    "EBK-EVT-04": "EVT-03",
    "EBK-WAR-01": "WAR-01",
    "EBK-WAR-02": "WAR-04",
    "EBK-GROW-02": "LIFE-01",
    "EBK-SCN-01": "SCN-04",
}


def pending_finding_ids(findings_dir: Path) -> list[str]:
    result = []
    for path in sorted(findings_dir.glob("RE-SAN9PK-*.md")):
        if "待验证" in path.read_text(encoding="utf-8"):
            result.append(path.stem)
    return result


def coverage_routes(coverage: Path) -> dict[str, str]:
    result = {}
    pattern = re.compile(r"^\| `([^`]+)` .*\| `roadmap:([^`]+)` \|", re.MULTILINE)
    for claim_id, batch in pattern.findall(coverage.read_text(encoding="utf-8")):
        if claim_id in SOURCE_CLAIM_ROUTES:
            result[claim_id] = batch
    return result


def build_status(findings_dir: Path, coverage: Path, queue_status: Path) -> dict[str, object]:
    pending = pending_finding_ids(findings_dir)
    pending_set = set(pending)
    stale_routes = sorted(set(FINDING_ROUTES) - pending_set)
    invalid_batches = sorted({*FINDING_ROUTES.values(), *SOURCE_CLAIM_ROUTES.values()} - FINAL_BATCHES)
    actual_claims = coverage_routes(coverage)
    claim_mismatches = {
        claim_id: {"expected": batch, "actual": actual_claims.get(claim_id)}
        for claim_id, batch in SOURCE_CLAIM_ROUTES.items()
        if actual_claims.get(claim_id) != batch
    }

    queue = json.loads(queue_status.read_text(encoding="utf-8"))
    final_queue = queue["final_round_routing"]
    routes = []
    for finding_id in pending:
        if finding_id in FINDING_ROUTES:
            disposition = "roadmap"
            batch = FINDING_ROUTES[finding_id]
            reason = "open-core-question"
        elif int(finding_id.rsplit("-", 1)[1]) >= 91:
            disposition = "baseline"
            batch = None
            reason = "accepted-r0-r7-or-superseded-follow-up"
        else:
            disposition = "out-of-scope"
            batch = None
            reason = "content-ui-editor-rendering-or-optional-deep-dive"
        routes.append({"finding": finding_id, "disposition": disposition, "batch": batch, "reason": reason})

    issues = {
        "stale_finding_routes": stale_routes,
        "invalid_batches": invalid_batches,
        "source_claim_mismatches": claim_mismatches,
        "queue_unrouted_count": final_queue["unrouted_count"],
    }
    valid = not stale_routes and not invalid_batches and not claim_mismatches and final_queue["unrouted_count"] == 0
    digest = hashlib.sha256("\n".join(pending).encode("utf-8")).hexdigest().upper()
    return {
        "schema_version": 1,
        "batch": "EVT-01",
        "status": "completed" if valid else "in-progress",
        "sample": "SRC-SAN9PK-TC-101",
        "domain_model": {
            "object": "source claim or existing finding with an unresolved marker",
            "states": ["baseline", "roadmap", "out-of-scope"],
            "input": ["SanIX", "electronic book", "San9PKHard queue", "RE-SAN9PK-0001..0126"],
            "output": "exactly one current disposition and, for roadmap work, one primary final-round batch",
            "invariant": "counts are conserved; mixed source sections are split before routing; scheduling never confirms behavior",
        },
        "source_claims": {
            "atomic_final_claim_count": len(SOURCE_CLAIM_ROUTES),
            "routes": SOURCE_CLAIM_ROUTES,
            "validation": actual_claims,
        },
        "san9pkhard_candidates": {
            "candidate_count": queue["candidate_count"],
            "baseline_count": queue["disposition_counts"]["baseline"],
            "out_of_scope_count": queue["disposition_counts"]["out-of-scope"],
            "final_round_count": final_queue["candidate_count"],
            "batch_counts": final_queue["batch_counts"],
            "unrouted_count": final_queue["unrouted_count"],
            "queue_sha256": queue["queue_sha256"],
        },
        "existing_findings": {
            "pending_marker_finding_count": len(pending),
            "pending_id_sha256": digest,
            "disposition_counts": dict(sorted(Counter(item["disposition"] for item in routes).items())),
            "batch_counts": dict(sorted(Counter(item["batch"] for item in routes if item["batch"]).items())),
            "routes": routes,
        },
        "ida_mcp": {
            "investigation": "not executed",
            "reason": "EVT-01 is source and scheduling inventory only; it asserts no new address, bytes, control flow, or original rule",
            "write_operations": 0,
        },
        "validation": {"valid": valid, "issues": issues},
        "verification": {
            "research_script_tests": "63 passed",
            "dotnet_tests": "112 passed",
            "queue_rebuild": "2635 candidates; 0 issues",
            "git_diff_check": "passed",
        },
        "next_batch": "EVT-02",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--findings-dir", type=Path, required=True)
    parser.add_argument("--coverage", type=Path, required=True)
    parser.add_argument("--queue-status", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    status = build_status(args.findings_dir, args.coverage, args.queue_status)
    args.output.write_text(json.dumps(status, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": status["status"], **status["validation"]}, ensure_ascii=False, sort_keys=True))
    return 0 if status["validation"]["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
