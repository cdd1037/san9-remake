"""Locate accepted missing glyphs in source and converted text, verifying original archive bytes."""
import argparse
import hashlib
import json
from pathlib import Path

from ls11_archive import decode_ls11, split_indexed_records

GLYPHS = (0x20860, 0x29166, 0x2AEFA, 0x2B623)


def literals(value, path=""):
    if isinstance(value, dict):
        if value.get("kind") == "literal":
            yield path, value
        for key, child in value.items():
            yield from literals(child, path + "/" + key)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from literals(child, path + "/" + str(index))


def audit(data_dir, exports):
    hits = []
    for catalog in ("messages", "base-messages", "biographies", "base-biographies"):
        evidence = json.loads((exports / f"{catalog}-audit.json").read_text(encoding="utf-8-sig"))
        messages = json.loads((exports / f"{catalog}.json").read_text(encoding="utf-8-sig"))
        raw = (data_dir / evidence["file"]).read_bytes()
        if hashlib.sha256(raw).hexdigest().upper() != evidence["sha256"]:
            raise ValueError("Archive hash mismatch")
        decoded, _ = decode_ls11(raw)
        _, _, records, offsets = split_indexed_records(decoded)
        for row in evidence["records"]:
            for path, literal in literals(messages[row["key"]]):
                for code in GLYPHS:
                    source = literal["sourceBbCode"].count(chr(code))
                    display = literal["bbCode"].count(chr(code))
                    if not (source or display):
                        continue
                    index = int(row["key"].rsplit(".", 1)[1])
                    if offsets[index] != row["offset"] or records[index] != bytes.fromhex(row["rawHex"]):
                        raise ValueError("Source record mismatch")
                    hits.append(dict(code=f"U+{code:X}", archive=evidence["file"], key=row["key"],
                                     decodedOffset=row["offset"], node=path, sourceCount=source,
                                     displayCount=display, sourceText=literal["sourceBbCode"],
                                     displayText=literal["bbCode"], rawHex=row["rawHex"]))
    return dict(scope="Four original message archives; decoded offsets verified against LS11 source records.", hits=hits)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("data_dir", type=Path)
    parser.add_argument("exports", type=Path)
    args = parser.parse_args()
    result = audit(args.data_dir, args.exports)
    (args.exports / "accepted-glyph-occurrences.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(result['hits'])} literal occurrences against original archive records.")
