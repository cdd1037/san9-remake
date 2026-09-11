"""Export registered source string bytes; San9.Tools owns all text conversion.

Run inside the registered TC-101 IDB. This script does not mutate the database.
"""
import hashlib
import json
import os
from pathlib import Path
import ida_bytes
import ida_funcs
import ida_nalt
import ida_segment
import idautils


def export(output: Path):
    source = Path(ida_nalt.get_input_file_path())
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest != "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028":
        raise ValueError("Not the registered TC-101 sample")
    rows = []
    for address in (a for start in idautils.Segments()
                    for a in idautils.Heads(start, ida_segment.getseg(start).end_ea)):
        if not ida_bytes.is_strlit(ida_bytes.get_full_flags(address)):
            continue
        kind = ida_nalt.get_str_type(address)
        if ida_nalt.get_str_type_code(kind) != 0 or ida_nalt.get_strtype_bpu(kind) != 1:
            continue
        # get_strlit_contents can transcode a Big5 IDA string to UTF-8; fetch source bytes instead.
        raw = ida_bytes.get_bytes(address, ida_bytes.get_item_size(address)).split(b"\0", 1)[0]
        refs = [{"address": hex(x.frm), "function": ida_funcs.get_func_name(x.frm)}
                for x in idautils.XrefsTo(address)]
        if raw is not None:
            rows.append({"address": hex(address), "rawHex": bytes(raw).hex(), "references": refs})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"sha256": digest, "strings": rows}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Registered string evidence: {len(rows)} -> {output}")


if __name__ == "__main__":
    export(Path(os.environ.get("SAN9_TEXT_EVIDENCE", str(Path(__file__).resolve().parents[3] / "local/generated/research/unified-text/exe-evidence.json"))))
