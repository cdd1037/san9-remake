"""Build a normalized address catalog from the local research sources.

Only Python's standard library is required. Generated JSON/CSV output belongs
under local/generated/research/ and is intentionally not tracked.
"""

from __future__ import annotations

import argparse
import ast
import csv
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path


ADDRESS_PATTERNS = (
    re.compile(r"\b(?P<name>m_[A-Za-z0-9_]*Addr[A-Za-z0-9_]*)\s*\((?P<expr>0x[0-9A-Fa-f]+(?:\s*[+*]\s*(?:0x[0-9A-Fa-f]+|\d+))*)\)"),
    re.compile(r"\b(?P<name>dwAddr[A-Za-z0-9_]*)\s*=\s*(?P<expr>0x[0-9A-Fa-f]+(?:\s*[+*]\s*(?:0x[0-9A-Fa-f]+|\d+))*)"),
    re.compile(r"^\s*#define\s+(?P<name>[A-Za-z0-9_]*ADDR[A-Za-z0-9_]*)\s+(?P<expr>0x[0-9A-Fa-f]+(?:\s*[+*]\s*(?:0x[0-9A-Fa-f]+|\d+))*)"),
    re.compile(r"^\s*(?P<name>[A-Z][A-Z0-9_]{2,})\s*=\s*(?P<expr>0x[0-9A-Fa-f]+)"),
)
INLINE_ADDRESS = re.compile(r"(?<![0-9A-Fa-f])0x(?P<address>[0-9A-Fa-f]{6,8})(?![0-9A-Fa-f])")


@dataclass(frozen=True)
class Candidate:
    address: str
    source_name: str
    kind: str
    module: str
    source_id: str
    source_path: str
    line: int
    expression: str
    context: str
    status: str = "pending"


def eval_int(expression: str) -> int:
    tree = ast.parse(expression, mode="eval")

    def visit(node: ast.AST) -> int:
        if isinstance(node, ast.Expression):
            return visit(node.body)
        if isinstance(node, ast.Constant) and isinstance(node.value, int):
            return node.value
        if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.Add, ast.Mult)):
            left, right = visit(node.left), visit(node.right)
            return left + right if isinstance(node.op, ast.Add) else left * right
        raise ValueError(f"unsupported expression: {expression}")

    return visit(tree)


def module_for(text: str) -> str:
    lowered = text.lower()
    groups = (
        ("core-data", ("person", "officer", "force", "city", "zone", "building", "array", "killer")),
        ("command", ("command", "search", "invite", "dispatch", "dialog", "select")),
        ("combat", ("attack", "damage", "wound", "troop", "battle", "retreat")),
        ("strategy", ("gossip", "alienate", "trick", "intelligence", "diplom")),
        ("ai", ("npc", "goal", "avoid", "decision")),
        ("event-ui", ("event", "display", "menu", "message", "caption")),
    )
    for module, words in groups:
        if any(word in lowered for word in words):
            return module
    return "unclassified"


def source_id(path: Path) -> str:
    value = str(path).replace("\\", "/").lower()
    if "san9pkhard" in value:
        return "SRC-SAN9PKHARD"
    if "jslhcl.github.io" in value:
        return "SRC-JSLHCL"
    return "SRC-DOCS"


def iter_candidates(workspace: Path):
    roots = (
        workspace / "private" / "research" / "upstream" / "San9PKHard",
        workspace / "private" / "research" / "upstream" / "jslhcl.github.io" / "files",
        workspace / "private" / "research" / "docs",
    )
    suffixes = {".c", ".cc", ".cpp", ".h", ".hpp", ".py", ".md", ".txt"}
    for root in roots:
        if not root.exists():
            continue
        for path in sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() in suffixes):
            # The archived assistant transcript repeats commands, source files, and
            # outputs thousands of times. Curated plan/README/scripts contain the
            # same usable leads without treating transcript echoes as candidates.
            if path.name.lower().startswith("copilot-session-"):
                continue
            try:
                lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
            except OSError:
                continue
            relative = path.relative_to(workspace).as_posix()
            sid = source_id(path)
            for number, line in enumerate(lines, 1):
                stripped = line.strip()
                seen: set[tuple[int, str]] = set()
                for pattern in ADDRESS_PATTERNS:
                    for match in pattern.finditer(line):
                        try:
                            address = eval_int(match.group("expr"))
                        except (SyntaxError, ValueError):
                            continue
                        name = match.group("name")
                        seen.add((address, name))
                        yield Candidate(
                            f"0x{address:08X}", name, "unknown", module_for(name + " " + stripped),
                            sid, relative, number, match.group("expr"), stripped[:300]
                        )
                for match in INLINE_ADDRESS.finditer(line):
                    address = int(match.group("address"), 16)
                    name = f"line_{number}_address"
                    if any(existing_address == address for existing_address, _ in seen):
                        continue
                    yield Candidate(
                        f"0x{address:08X}", name, "unknown", module_for(stripped), sid,
                        relative, number, match.group(0), stripped[:300]
                    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, default=Path.cwd())
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--csv", type=Path, required=True)
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    rows = sorted(
        {
            (row.address, row.source_name, row.source_id): row
            for row in iter_candidates(workspace)
        }.values(),
        key=lambda row: (int(row.address, 16), row.source_id, row.source_path, row.line),
    )
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.csv.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps([asdict(row) for row in rows], ensure_ascii=False, indent=2), encoding="utf-8")
    with args.csv.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=Candidate.__dataclass_fields__.keys())
        writer.writeheader()
        writer.writerows(asdict(row) for row in rows)
    print(f"candidates={len(rows)} json={args.json} csv={args.csv}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
