"""Split a reconciled IDA candidate catalog into ordered dry-run batches."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


BATCHES = (
    ("01-runtime-common", {"runtime", "runtime-container", "legacy-idb"}),
    ("02-core-records", {"core-data"}),
    ("03-command-ui", {"command", "command-ui", "ui-selector"}),
    ("04-domestic-diplomacy-strategy", {"domestic", "talent", "diplomacy", "strategy"}),
    ("05-combat-logistics", {"combat", "logistics", "facility"}),
    ("06-ai-events-remaining-ui", {"ai", "event-ui", "unclassified"}),
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("catalog", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    payload = json.loads(args.catalog.read_text(encoding="utf-8"))
    candidates = payload.get("candidates", payload)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    assigned: set[int] = set()
    summary = []
    for batch_name, modules in BATCHES:
        rows = []
        for index, row in enumerate(candidates):
            if index in assigned or row.get("module") not in modules:
                continue
            rows.append(row)
            assigned.add(index)
        counts: dict[str, int] = {}
        for row in rows:
            status = row.get("status", "pending")
            counts[status] = counts.get(status, 0) + 1
        output = args.output_dir / f"{batch_name}.json"
        output.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "mode": "dry-run",
                    "batch": batch_name,
                    "modules": sorted(modules),
                    "status_counts": counts,
                    "candidates": rows,
                },
                ensure_ascii=False,
                indent=2,
            ),
            encoding="utf-8",
        )
        summary.append({"batch": batch_name, "count": len(rows), "statuses": counts})
    if len(assigned) != len(candidates):
        missing = [row for index, row in enumerate(candidates) if index not in assigned]
        raise RuntimeError(f"{len(missing)} candidates were not assigned to a batch")
    print(json.dumps(summary, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
