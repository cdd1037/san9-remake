"""Build and validate the program-analysis candidate queue.

The queue is a scheduling artifact, not an original-behavior specification.
It classifies every active COV-02 observation exactly once and keeps restore
entries, byte arrays, old values, and file inventory as supporting coverage.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Iterable

from build_san9pkhard_catalog import build_catalog, build_status, decode_source


ALLOWED_STATUSES = {
    "planned", "in-progress", "confirmed", "inferred", "conflict",
    "experiment-required", "version-inapplicable", "blocked", "completed",
}
ALLOWED_DISPOSITIONS = {"roadmap", "baseline", "out-of-scope"}
ALLOWED_BATCHES = {
    "DOM-01", "DOM-02", "DOM-03", "DOM-04",
    "TAC-01", "TAC-02", "TAC-03", "TAC-04",
    "DUEL-01", "DUEL-02", "DUEL-03", "DUEL-04",
    "PERS-02", "PERS-03", "PERS-04",
    "STR-01", "STR-02", "STR-03", "STR-04",
    "DIP-01", "DIP-02", "DIP-03", "DIP-04",
    "GOV-01", "GOV-02", "GOV-03", "GOV-04",
    "EVT-02", "EVT-03", "EVT-04",
    "LIFE-01", "LIFE-02", "LIFE-03", "LIFE-04",
    "WAR-01", "WAR-02", "WAR-03", "WAR-04",
    "SCN-01", "SCN-02", "SCN-03", "SCN-04",
    "CLOSE-01", "CLOSE-02", "CLOSE-03", "CLOSE-04",
}
ADDRESS_PATTERN = re.compile(r"^0x[0-9A-F]+$")
IMAGE_START = 0x400000
IMAGE_END = 0x1B59000
ALLOWED_ADDRESS_KINDS = {
    "image-address-candidate",
    "relative-offset-or-count",
    "runtime-or-injected-data",
    "dynamic-or-patch-structure-write",
    "semantic-only",
}


SUBSYSTEM_RULES = (
    ("duel", ("duel", "slash", "all person dead", "general dead", "kill person", "person death")),
    ("domestic", ("commerce", "cultivate", "draft", "train", "repair", "population", "harvest", "income", "municipal", "peopleheart", "trade", "buy", "sell")),
    ("tactics", ("killer", "tactic", "proficiency", "linkage", "inspire", "chaos day", "wisdom resist", "lower trick", "lower trap", "block array")),
    ("tactical-ai", ("dispatch", "retreat", "attack", "defence", "defense", "goal", "target", "troop", "array", "building", "npc", "barbar")),
    ("personnel-diplomacy", ("alienate", "gossip", "invite", "loyal", "league", "friendship", "counselor", "counsellor", "official", "search", "award")),
    ("events", ("event", "huatuo", "year", "story")),
    ("ui-system", ("display", "dialog", "interface", "menu", "view", "resource", "message", "page", "caption", "warfog")),
)


def source_context(source_root: Path, source: str, line: int, cache: dict[str, list[str]]) -> str:
    if source not in cache:
        path = source_root / source
        cache[source] = decode_source(path.read_bytes()).splitlines() if path.exists() else []
    lines = cache[source]
    if not lines:
        return ""
    start, end = max(0, line - 3), min(len(lines), line)
    return " ".join(lines[start:end])


def normalized_words(text: str) -> str:
    expanded = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", text)
    expanded = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", expanded)
    return " ".join(re.findall(r"[a-z0-9]+", expanded.lower()))


def has_term(text: str, term: str) -> bool:
    words = f" {normalized_words(text)} "
    needle = f" {normalized_words(term)} "
    return needle in words


def classify_subsystem(search_text: str) -> tuple[str, str]:
    for subsystem, keywords in SUBSYSTEM_RULES:
        matches = [keyword for keyword in keywords if has_term(search_text, keyword)]
        if matches:
            return subsystem, matches[0]
    return "infrastructure", "fallback-no-domain-keyword"


def route_candidate(subsystem: str, search_text: str) -> tuple[str, str, str | None, str]:
    # R0-R7 have passed independent acceptance.  The queue is the current
    # schedule, so completed domains must no longer appear as planned work.
    # Strong final-round terms are routed before the historical subsystem
    # buckets because many San9PKHard identifiers were originally classified
    # only as generic infrastructure or tactical AI.
    if any(has_term(search_text, word) for word in ("game end", "end year", "victory", "defeat", "ending")):
        return "planned", "roadmap", "SCN-03", "terminal-state-or-end-condition-candidate"
    if any(has_term(search_text, word) for word in ("emperor", "title", "successor", "succession")):
        return "planned", "roadmap", "SCN-02", "imperial-title-or-succession-candidate"
    if any(has_term(search_text, word) for word in ("transport", "convoy")):
        return "planned", "roadmap", "WAR-02", "transport-command-candidate"
    if any(has_term(search_text, word) for word in ("durability", "siege")):
        return "planned", "roadmap", "WAR-03", "siege-or-durability-candidate"
    if any(has_term(search_text, word) for word in ("move again", "move guard", "path", "movement")):
        return "planned", "roadmap", "WAR-01", "movement-or-path-candidate"
    if any(has_term(search_text, word) for word in ("all person dead", "general dead")):
        return "planned", "roadmap", "WAR-04", "battle-death-or-unit-cleanup-candidate"
    if any(has_term(search_text, word) for word in ("hua tuo", "search event", "story")):
        return "planned", "roadmap", "EVT-03", "representative-event-family-candidate"
    if subsystem == "events":
        if any(has_term(search_text, word) for word in ("year table", "display year")):
            return "completed", "baseline", None, "calendar-boundary-closed-by-close-02"
        return "planned", "roadmap", "EVT-02", "event-engine-or-state-candidate"
    if any(has_term(search_text, word) for word in ("lifespan", "life span", "natural death", "injury", "recover")):
        return "planned", "roadmap", "LIFE-02", "injury-lifespan-or-natural-death-candidate"
    if any(has_term(search_text, word) for word in ("learn tactic", "learn killer", "ability growth", "proficiency growth")):
        return "planned", "roadmap", "LIFE-01", "growth-or-learning-candidate"
    if any(has_term(search_text, word) for word in ("cadet", "trainee", "minor officer")):
        return "planned", "roadmap", "LIFE-03", "cadet-training-candidate"
    if subsystem == "domestic":
        return "completed", "baseline", None, "domestic-coverage-closed-by-r1"
    if subsystem == "tactics":
        return "completed", "baseline", None, "tactic-coverage-closed-by-r2"
    if subsystem == "duel":
        return "completed", "baseline", None, "duel-coverage-closed-by-r3"
    if subsystem == "tactical-ai":
        return "completed", "baseline", None, "tactical-ai-coverage-closed-by-existing-baseline"
    if subsystem == "personnel-diplomacy":
        return "completed", "baseline", None, "personnel-diplomacy-coverage-closed-by-r4-r7"
    return "inferred", "out-of-scope", None, "outside-active-roadmap-or-infrastructure"


def normalized_source_ref(item: dict[str, object]) -> dict[str, object]:
    return {"file": str(item["source"]), "line": int(item["line"])}


def make_candidate(
    index: int,
    kind: str,
    name: str,
    item: dict[str, object],
    search_text: str,
    source_refs: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    subsystem, matched_rule = classify_subsystem(search_text)
    status, disposition, batch, route_reason = route_candidate(subsystem, search_text)
    addresses = sorted({str(value) for value in (item.get("address"),) if value})
    if addresses:
        address_kind = str(item.get("classification") or "image-address-candidate")
    elif kind in {"config-binding", "config-bit"}:
        address_kind = "semantic-only"
    else:
        address_kind = str(item.get("classification") or "dynamic-or-patch-structure-write")
    missing_reason = None if addresses else str(item.get("exclusion_reason") or "dynamic-or-semantic-only")
    return {
        "candidate_id": f"HARD-Q-{index:05d}",
        "kind": kind,
        "name": name,
        "subsystem": subsystem,
        "classification_rule": matched_rule,
        "status": status,
        "disposition": disposition,
        "batch": batch,
        "route_reason": route_reason,
        "source_id": "SRC-SAN9PKHARD",
        "source_refs": source_refs or [normalized_source_ref(item)],
        "addresses": addresses,
        "address_kind": address_kind,
        "missing_address_reason": missing_reason,
    }


def build_queue(catalog: dict[str, object], source_root: Path) -> dict[str, object]:
    sections = catalog["sections"]
    candidates = []
    index = 1
    context_cache: dict[str, list[str]] = {}

    def add(kind: str, name: str, item: dict[str, object], text: str, refs=None) -> None:
        nonlocal index
        context = source_context(source_root, str(item["source"]), int(item["line"]), context_cache)
        # A nearby comment often belongs to the preceding patch block.  Use
        # it only when the candidate's own identifiers have no domain signal.
        own_subsystem, _ = classify_subsystem(text)
        use_context = kind == "process-write" and own_subsystem == "infrastructure"
        search_text = f"{text} {context}" if use_context else text
        candidates.append(make_candidate(index, kind, name, item, search_text, refs))
        index += 1

    for item in sections["address_symbols"]:
        if item["active"]:
            add("address-symbol", str(item["symbol"]), item, f"{item['symbol']} {item['expression']}")
    for item in sections["address_array_entries"]:
        if item["active"]:
            add("address-array-entry", f"{item['symbol']}[{item['index']}]", item, str(item["symbol"]))

    restores: defaultdict[tuple[str, str], list[dict[str, object]]] = defaultdict(list)
    for item in sections["patch_entries"]:
        if item["active"] and item["role"] == "restore-old-bytes":
            restores[(str(item["patch"]), str(item["address"]))].append(item)
    for item in sections["patch_entries"]:
        if item["active"] and item["role"] == "install-replacement":
            paired = restores.get((str(item["patch"]), str(item["address"])), [])
            refs = [normalized_source_ref(item), *(normalized_source_ref(old) for old in paired)]
            add("static-patch", str(item["patch"]), item, f"{item['patch']} {item['code_symbol']}", refs)

    for item in sections["write_calls"]:
        if item["active"]:
            symbols = " ".join(str(value) for value in item["destination_symbols"])
            name = symbols or str(item["destination_expression"] or "literal-or-dynamic-write")
            add("process-write", name, item, f"{name} {item['data_expression']} {item['destination_expression']}")
    for item in sections["config_bindings"]:
        if item["active"]:
            add("config-binding", str(item["field"]), item, f"{item['field']} {item['control']} {item['binding']}")
    for item in sections["config_bit_mappings"]:
        if item["active"]:
            add("config-bit", f"{item['field']}:{item['direction']}:{item['bit']}", item, str(item["field"]))

    supporting_coverage = {
        "inactive_address_symbols": sum(not item["active"] for item in sections["address_symbols"]),
        "inactive_address_array_entries": sum(not item["active"] for item in sections["address_array_entries"]),
        "patch_restore_entries": sum(item["active"] and item["role"] == "restore-old-bytes" for item in sections["patch_entries"]),
        "inactive_patch_entries": sum(not item["active"] for item in sections["patch_entries"]),
        "byte_arrays": len(sections["byte_arrays"]),
        "old_value_symbols": len(sections["old_value_symbols"]),
        "old_byte_records": len(sections["old_byte_records"]),
        "config_files": len(sections["config_files"]),
        "file_inventory": len(sections["file_inventory"]),
    }
    expected_candidates = (
        sum(item["active"] for item in sections["address_symbols"])
        + sum(item["active"] for item in sections["address_array_entries"])
        + sum(item["active"] and item["role"] == "install-replacement" for item in sections["patch_entries"])
        + sum(item["active"] for item in sections["write_calls"])
        + sum(item["active"] for item in sections["config_bindings"])
        + sum(item["active"] for item in sections["config_bit_mappings"])
    )
    return {
        "schema_version": 2,
        "source_id": "SRC-SAN9PKHARD",
        "catalog_sha256": build_status(catalog)["catalog_sha256"],
        "expected_candidate_count": expected_candidates,
        "supporting_coverage": supporting_coverage,
        "candidates": candidates,
    }


def validate_queue(queue: dict[str, object], source_root: Path) -> dict[str, object]:
    candidates = queue["candidates"]
    issues: defaultdict[str, list[dict[str, object]]] = defaultdict(list)
    ids = Counter(str(item["candidate_id"]) for item in candidates)
    for candidate_id, count in ids.items():
        if count != 1:
            issues["duplicate_ids"].append({"candidate_id": candidate_id, "count": count})
    line_counts: dict[str, int] = {}
    for item in candidates:
        candidate_id = str(item["candidate_id"])
        if item["status"] not in ALLOWED_STATUSES:
            issues["invalid_status"].append({"candidate_id": candidate_id, "value": item["status"]})
        if item["disposition"] not in ALLOWED_DISPOSITIONS:
            issues["invalid_disposition"].append({"candidate_id": candidate_id, "value": item["disposition"]})
        if item["disposition"] == "roadmap" and item["batch"] not in ALLOWED_BATCHES:
            issues["invalid_batch"].append({"candidate_id": candidate_id, "value": item["batch"]})
        if item["disposition"] != "roadmap" and item["batch"] is not None:
            issues["unexpected_batch"].append({"candidate_id": candidate_id, "value": item["batch"]})
        if not item["subsystem"]:
            issues["missing_subsystem"].append({"candidate_id": candidate_id})
        if item["address_kind"] not in ALLOWED_ADDRESS_KINDS:
            issues["invalid_address_kind"].append({"candidate_id": candidate_id, "value": item["address_kind"]})
        for address in item["addresses"]:
            if not ADDRESS_PATTERN.fullmatch(str(address)):
                issues["invalid_address"].append({"candidate_id": candidate_id, "value": address})
            elif item["address_kind"] == "image-address-candidate" and not IMAGE_START <= int(str(address), 16) < IMAGE_END:
                issues["image_address_out_of_range"].append({"candidate_id": candidate_id, "value": address})
        if not item["addresses"] and not item["missing_address_reason"]:
            issues["missing_address_reason"].append({"candidate_id": candidate_id})
        if item["source_id"] != "SRC-SAN9PKHARD":
            issues["invalid_source_id"].append({"candidate_id": candidate_id, "value": item["source_id"]})
        for ref in item["source_refs"]:
            relative = str(ref["file"])
            path = source_root / relative
            if not path.is_file():
                issues["missing_source_file"].append({"candidate_id": candidate_id, "file": relative})
                continue
            if relative not in line_counts:
                line_counts[relative] = len(decode_source(path.read_bytes()).splitlines())
            if not 1 <= int(ref["line"]) <= line_counts[relative]:
                issues["invalid_source_line"].append({"candidate_id": candidate_id, **ref})
    if len(candidates) != int(queue["expected_candidate_count"]):
        issues["silent_omission"].append({
            "expected": queue["expected_candidate_count"],
            "actual": len(candidates),
        })
    by_address: defaultdict[str, list[str]] = defaultdict(list)
    for item in candidates:
        for address in item["addresses"]:
            by_address[str(address)].append(str(item["candidate_id"]))
    duplicate_addresses = [
        {"address": address, "candidate_count": len(candidate_ids)}
        for address, candidate_ids in sorted(by_address.items()) if len(candidate_ids) > 1
    ]
    return {
        "valid": not issues,
        "issue_counts": {name: len(values) for name, values in sorted(issues.items())},
        "issues": {name: values for name, values in sorted(issues.items())},
        "duplicate_address_count": len(duplicate_addresses),
        "duplicate_address_sha256": hashlib.sha256(
            json.dumps(duplicate_addresses, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest().upper(),
    }


def build_queue_status(queue: dict[str, object], validation: dict[str, object]) -> dict[str, object]:
    candidates = queue["candidates"]
    personnel = [item for item in candidates if item["subsystem"] == "personnel-diplomacy"]
    canonical = json.dumps(queue, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return {
        "schema_version": 2,
        "source_id": queue["source_id"],
        "catalog_sha256": queue["catalog_sha256"],
        "queue_sha256": hashlib.sha256(canonical).hexdigest().upper(),
        "candidate_count": len(candidates),
        "expected_candidate_count": queue["expected_candidate_count"],
        "kind_counts": dict(sorted(Counter(str(item["kind"]) for item in candidates).items())),
        "subsystem_counts": dict(sorted(Counter(str(item["subsystem"]) for item in candidates).items())),
        "status_counts": dict(sorted(Counter(str(item["status"]) for item in candidates).items())),
        "disposition_counts": dict(sorted(Counter(str(item["disposition"]) for item in candidates).items())),
        "batch_counts": dict(sorted(Counter(str(item["batch"]) for item in candidates if item["batch"]).items())),
        "personnel_diplomacy_routing": {
            "candidate_count": len(personnel),
            "disposition_counts": dict(sorted(Counter(str(item["disposition"]) for item in personnel).items())),
            "batch_counts": dict(sorted(Counter(str(item["batch"]) for item in personnel if item["batch"]).items())),
            "unrouted_count": sum(item["batch"] is None and item["disposition"] == "roadmap" for item in personnel),
        },
        "final_round_routing": {
            "candidate_count": sum(item["disposition"] == "roadmap" for item in candidates),
            "batch_counts": dict(sorted(Counter(
                str(item["batch"]) for item in candidates
                if item["disposition"] == "roadmap"
            ).items())),
            "unrouted_count": sum(item["disposition"] == "roadmap" and item["batch"] is None for item in candidates),
        },
        "address_kind_counts": dict(sorted(Counter(str(item["address_kind"]) for item in candidates).items())),
        "supporting_coverage": queue["supporting_coverage"],
        "validation": validation,
        "ida_mcp_audit": "research/program-analysis-queue-ida-audit.json",
        "reproduce": "python research/scripts/build_program_analysis_queue.py --source-root research/upsteam/San9PKHard --catalog-status research/patch-points/san9pkhard-catalog-status.json --output tmp/program-analysis-queue.json --status-output research/program-analysis-queue-status.json",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--catalog-status", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--status-output", type=Path, required=True)
    args = parser.parse_args()
    catalog = build_catalog(args.source_root)
    expected_catalog_status = json.loads(args.catalog_status.read_text(encoding="utf-8"))
    actual_catalog_sha = build_status(catalog)["catalog_sha256"]
    if actual_catalog_sha != expected_catalog_status["catalog_sha256"]:
        raise SystemExit("COV-02 catalog/source digest mismatch; rebuild and review COV-02 first")
    queue = build_queue(catalog, args.source_root)
    validation = validate_queue(queue, args.source_root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(queue, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    status = build_queue_status(queue, validation)
    args.status_output.parent.mkdir(parents=True, exist_ok=True)
    args.status_output.write_text(json.dumps(status, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"candidate_count": len(queue["candidates"]), **validation}, ensure_ascii=False, sort_keys=True))
    return 0 if validation["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
