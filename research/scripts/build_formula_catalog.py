"""Build the static-analysis catalog for the documented San9 PK formulas.

The generated files are research indexes, not confirmed behavior.  They keep
the claimed original formulas separate from the San9PKHard improvements and
merge only explicit, reviewed status overrides.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path


VALID_STATUSES = {
    "unlocated",
    "address-match",
    "signature-migrated",
    "original-confirmed",
    "patch-confirmed",
    "conflict",
    "version-inapplicable",
    "experiment-required",
}

FORMULA_HEADING = re.compile(r"^###\s+(?P<number>\d+)\.(?P<title>.+?)\s*$")
PATCH_INIT = re.compile(
    r"\b(?P<name>m_[A-Za-z0-9_]+)\s*\(\s*(?P<value>0x[0-9A-Fa-f]+|\d+)\s*\)"
)

BATCHES = {
    **{number: "domestic-strategy" for number in range(0, 15)},
    15: "normal-combat",
    16: "normal-combat",
    **{number: "tactics" for number in range(17, 25)},
    25: "person-resolution",
    26: "tactics",
    27: "person-resolution",
    28: "tactics",
    29: "tactics",
}

# Explicit semantic links only.  A matching variable name is still merely a
# lead until its old bytes and containing target function are verified.
PATCH_LINKS = {
    0: ["m_dwOfficerBasicCommandAddr", "m_dwOfficerBasicIntAddr"],
    1: ["m_dwCounselorMinCandidateAddr"],
    2: ["m_dwInviteSubLoyatyAddr", "m_dwInviteAddLoyatyAddr"],
    3: ["m_dwAlienateIntelligenceAddr"],
    4: ["m_dwGossipIntelligenceAddr", "m_dwGossipNoAlienateAddr", "m_dwGossipSubLoyaltyAddr"],
    5: ["m_dwInspireIntelligenceAddr", "m_dwInspireMaxVigourAddr"],
    6: ["m_dwCheatIntelligenceAddr"],
    7: ["m_dwDisorderIntelligenceAddr"],
    8: ["m_dwBurnFoodProbabilityAddr", "m_dwBurnFoodEffectAddr", "m_dwBurnBuildingEffectAddr"],
    10: ["m_dwReinforceExperienceAddr"],
    15: ["m_dwBuildingDefenseCoefAddr", "patchGetArrayToGarrisonThunk"],
    16: ["patchGetArrayToWallThunk", "patchMinDurabilityThunk"],
    17: ["patchFightToKillerThunk"],
    19: ["m_dwFootAntiElephantAddr"],
    20: ["patchKillerPowerToDamageThunk", "patchUpdateKillerDamageThunk"],
    22: ["m_dwTroopWoundAddr", "m_dwBuildingWoundAddr"],
    24: ["m_dwChaosDayAddr", "m_dwWisdomResistTrickAddr", "m_dwLowerTrickAddr", "m_dwLowerTrapAddr"],
    27: ["m_dwSlashDeadCoefAddr", "m_dwAllPersonDead1Addr", "m_dwAllPersonDead2Addr", "m_dwAllPersonDead3Addr"],
    28: ["m_dwMaxExperienceAddr", "patchPersonExperienceThunk", "patchKillerExperienceThunk", "patchWaterKillerExperienceThunk"],
    29: ["patchDynamicAddProficiencyThunk", "m_dwMaxBasicAbilityAddr"],
}

EBOOK_LINKS = {
    2: ["3-8.三国志IX 登用成功条件"],
    14: ["3-6.三国志9中决定兵役人口的因素", "6-3.人口 兵役（红黑）"],
    17: ["6-12.三国志9兵法发动之最小间隔", "6-14.三国志九兵法发动机理揭密"],
    18: ["4-3.兵法联动及武将配对", "6-13.联动兵法威力初探"],
    28: ["6-23.31兵法全习得经验谈", "7-3.兵法精通与兵法学得"],
    29: ["3-9.三国志IX 能力上升法"],
}


@dataclass(frozen=True)
class SourceSection:
    line: int
    original: str
    improvement: str | None


def extract_formula_sections(text: str) -> dict[int, tuple[str, SourceSection]]:
    lines = text.splitlines()
    headings: list[tuple[int, str, int]] = []
    for index, line in enumerate(lines):
        match = FORMULA_HEADING.match(line)
        if match:
            headings.append((int(match.group("number")), match.group("title"), index))
    result: dict[int, tuple[str, SourceSection]] = {}
    for position, (number, title, start) in enumerate(headings):
        end = headings[position + 1][2] if position + 1 < len(headings) else len(lines)
        body = lines[start + 1 : end]
        try:
            divider = body.index("#### 优化方法")
        except ValueError:
            original_lines, improvement_lines = body, []
        else:
            original_lines = body[:divider]
            improvement_lines = body[divider + 1 :]
        original = "\n".join(original_lines).strip()
        improvement = "\n".join(improvement_lines).strip() or None
        result[number] = (title, SourceSection(start + 1, original, improvement))
    expected = set(range(30))
    if set(result) != expected:
        raise ValueError(f"expected formula headings 0..29, got {sorted(result)}")
    return result


def extract_patch_initializers(text: str) -> dict[str, dict[str, str | int]]:
    result: dict[str, dict[str, str | int]] = {}
    for line_number, line in enumerate(text.splitlines(), 1):
        for match in PATCH_INIT.finditer(line):
            name = match.group("name")
            if name in result:
                continue
            result[name] = {
                "expression": match.group("value"),
                "value": int(match.group("value"), 0),
                "line": line_number,
            }
    return result


def load_patch_manifest(path: Path) -> dict[str, dict[str, object]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    result: dict[str, dict[str, object]] = {}
    for point in data.get("points", []):
        name = str(point["name"])
        if name in result:
            raise ValueError(f"duplicate patch manifest point: {name}")
        result[name] = {
            "address": str(point["address"]),
            "expected_old_hex": str(point["expected_old_hex"]),
        }
    return result


def load_statuses(path: Path | None) -> dict[str, dict[str, object]]:
    if path is None or not path.exists():
        return {}
    rows = json.loads(path.read_text(encoding="utf-8"))
    result: dict[str, dict[str, object]] = {}
    for row in rows:
        claim_id = str(row["claim_id"])
        status = str(row["status"])
        if status not in VALID_STATUSES:
            raise ValueError(f"invalid status for {claim_id}: {status}")
        if claim_id in result:
            raise ValueError(f"duplicate status override: {claim_id}")
        result[claim_id] = row
    return result


def find_markdown_heading_lines(text: str) -> dict[str, int]:
    result: dict[str, int] = {}
    for line_number, line in enumerate(text.splitlines(), 1):
        if line.startswith("### "):
            result[line[4:].strip()] = line_number
    return result


def build_rows(workspace: Path, status_file: Path | None = None) -> list[dict[str, object]]:
    formula_path = workspace / "private/research/docs/程序分析、设计公式、优化方法/三国志9威力加强版的设计公式和优化方法.md"
    ebook_path = workspace / "private/research/docs/三国志9电子书.md"
    patch_path = workspace / "private/research/upstream/San9PKHard/San9PKHardDlg.cpp"
    patch_manifest_path = workspace / "research/patch-points/san9pkhard-formulas.json"
    sections = extract_formula_sections(formula_path.read_text(encoding="utf-8"))
    ebook_lines = find_markdown_heading_lines(ebook_path.read_text(encoding="utf-8"))
    patch_values = extract_patch_initializers(patch_path.read_text(encoding="utf-8", errors="replace"))
    patch_manifest_values = load_patch_manifest(patch_manifest_path)
    statuses = load_statuses(status_file)
    rows: list[dict[str, object]] = []
    for number in range(30):
        title, section = sections[number]
        claim_id = f"FML-SAN9PK-{number:04d}"
        override = statuses.get(claim_id, {})
        patch_points = []
        for name in PATCH_LINKS.get(number, []):
            value = patch_values.get(name) or patch_manifest_values.get(name)
            patch_points.append({"name": name, **value} if value else {"name": name, "missing": True})
        ebook_sources = []
        for heading in EBOOK_LINKS.get(number, []):
            ebook_sources.append({"heading": heading, "line": ebook_lines.get(heading)})
        rows.append(
            {
                "claim_id": claim_id,
                "formula_number": number,
                "title": title,
                "batch": BATCHES[number],
                "claim_class": "claimed-original",
                "source_path": formula_path.relative_to(workspace).as_posix(),
                "source_line": section.line,
                "source_text": section.original,
                "improvement_claim_id": f"IMP-SAN9PK-{number:04d}" if section.improvement else None,
                "improvement_text": section.improvement,
                "ebook_sources": ebook_sources,
                "patch_points": patch_points,
                "status": override.get("status", "unlocated"),
                "current_addresses": override.get("current_addresses", []),
                "target_functions": override.get("target_functions", []),
                "evidence": override.get("evidence", []),
                "missing_evidence": override.get("missing_evidence", []),
                "recommended_experiment": override.get("recommended_experiment"),
            }
        )
    unknown = sorted(set(statuses) - {str(row["claim_id"]) for row in rows})
    if unknown:
        raise ValueError(f"status overrides do not match formula claims: {unknown}")
    return rows


def write_outputs(rows: list[dict[str, object]], json_path: Path, csv_path: Path) -> None:
    json_path.parent.mkdir(parents=True, exist_ok=True)
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    scalar_fields = (
        "claim_id", "formula_number", "title", "batch", "claim_class", "source_path",
        "source_line", "status", "improvement_claim_id", "current_addresses",
        "target_functions", "patch_points", "ebook_sources", "missing_evidence",
        "recommended_experiment",
    )
    with csv_path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=scalar_fields)
        writer.writeheader()
        for row in rows:
            writer.writerow({key: json.dumps(row[key], ensure_ascii=False) if isinstance(row[key], (list, dict)) else row[key] for key in scalar_fields})


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, default=Path.cwd())
    parser.add_argument("--status-file", type=Path)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    args = parser.parse_args()
    rows = build_rows(args.workspace.resolve(), args.status_file)
    write_outputs(rows, args.json, args.csv)
    counts = {status: sum(row["status"] == status for row in rows) for status in sorted(VALID_STATUSES)}
    print(json.dumps({"claims": len(rows), "statuses": counts}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
