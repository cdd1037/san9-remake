"""Independent archive boundaries/raw-byte verification; no message-script parser."""
import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path

from ls11_archive import decode_ls11, split_indexed_records


def verify(data_dir: Path, output: Path) -> dict:
    archives = []
    for catalog in ("messages", "biographies", "base-messages", "base-biographies"):
        audit = json.loads((output / f"{catalog}-audit.json").read_text(encoding="utf-8-sig"))
        messages = json.loads((output / f"{catalog}.json").read_text(encoding="utf-8-sig"))
        raw = (data_dir / audit["file"]).read_bytes()
        assert hashlib.sha256(raw).hexdigest().upper() == audit["sha256"]
        decoded, _ = decode_ls11(raw)
        count, _, records, offsets = split_indexed_records(decoded)
        trailer = bytes.fromhex(audit["trailerHex"])
        if trailer:
            assert trailer == b"\0" and records[-1].endswith(b"\x05\x05\x05" + trailer)
            records[-1] = records[-1][:-len(trailer)]
        assert count == len(audit["records"]) == len(messages)
        sampled = []
        for index, (record, offset, row) in enumerate(zip(records, offsets, audit["records"], strict=True)):
            assert row["key"] == f"{catalog}.{index}"
            assert row["offset"] == offset and row["length"] == len(record)
            assert bytes.fromhex(row["rawHex"]) == record
            assert 0 <= row["consumedBytes"] <= len(record)
            if row["fullyParsed"]:
                assert row["consumedBytes"] == len(record)
            # Periodic independent plain CP950 samples; operands never decoded as text.
            body = record[:-3] if record.endswith(b"\x05\x05\x05") else record
            if index % 127 or any(b < 32 for b in body) or row["encoding"] != "cp950":
                continue
            try:
                expected = body.decode("cp950", errors="strict").replace("[", "[lb]")
            except UnicodeDecodeError:
                continue
            actual = "".join(n["sourceBbCode"] for n in messages[row["key"]]["nodes"] if n["kind"] == "literal")
            assert actual == expected, row["key"]
            sampled.append(row["key"])
        rows = audit["records"]
        archives.append({"catalog": catalog, "records": count, "raw_bytes_matched": count,
            "container_trailer_hex": audit["trailerHex"],
            "plain_source_samples": sampled,
            "fully_parsed": sum(r["fullyParsed"] for r in rows),
            "semantics_confirmed": sum(r["semanticsConfirmed"] for r in rows),
            "dependencies_confirmed": sum(r["dependenciesConfirmed"] for r in rows),
            "callable_without_gameplay_bindings": sum(r["dependenciesConfirmed"] and r["bindingFree"] for r in rows),
            "incomplete": [r["key"] for r in rows if not r["fullyParsed"]],
            "issues": dict(Counter(i["reason"] for r in rows for i in r["issues"])),
            "required_gameplay_bindings": sorted({b for r in rows for b in r["requiredBindings"]})})
    return {"scope": "Independent LS11/raw bytes and periodic plain CP950 samples; script semantics require IDA evidence.", "archives": archives}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("data_dir", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = verify(args.data_dir, args.output)
    (args.output / "coverage.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Verified {sum(a['records'] for a in report['archives'])} original records.")
