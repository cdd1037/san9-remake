"""Read-only whole-IDB style call-site inventory; never infer complete styles from isolated writes."""
import hashlib
import json
from pathlib import Path
import ida_bytes
import ida_funcs
import ida_hexrays
import ida_lines
import ida_name
import ida_nalt
import ida_segment
import idautils

TARGETS = {
    0x5BA1D0: ("foreground", 1), 0x5BA2B0: ("shadow", 1), 0x5BA350: ("outline", 1),
    0x5BA3F0: ("palette", 1), 0x427F90: ("button-outline-table", 1),
    0x427E50: ("button-foreground", 2), 0x427770: ("text-layout", 4),
    0x427E20: ("button-layout", 4), 0x58C130: ("foreground-wrapper", 1),
    0x411510: ("escape-normalization", 1), 0x411640: ("style-lookup", 0),
    0x427F60: ("button-foreground-table", 1), 0x427F80: ("button-shadow-table", 1),
    0x427E80: ("button-foreground-table-five", 1), 0x427EC0: ("button-shadow-table-five", 1),
    0x427F10: ("button-outline-table-five", 1), 0x427FC0: ("button-shadow", 1),
    0x427FE0: ("button-outline", 1),
}


def unwrap(expression):
    while expression.op in (ida_hexrays.cot_cast, ida_hexrays.cot_ref):
        expression = expression.x
    return expression


class Calls(ida_hexrays.ctree_visitor_t):
    def __init__(self):
        super().__init__(ida_hexrays.CV_FAST)
        self.rows = []

    def visit_expr(self, e):
        if e.op != ida_hexrays.cot_call or e.x.op != ida_hexrays.cot_obj or e.x.obj_ea not in TARGETS:
            return 0
        channel, index = TARGETS[e.x.obj_ea]
        row = dict(address=hex(e.ea), target=hex(e.x.obj_ea), channel=channel,
                   receiver=ida_lines.tag_remove(unwrap(e.a[0]).print1(None)) if len(e.a) else None,
                   arguments=[ida_lines.tag_remove(a.print1(None)) for a in e.a], status="dynamic-or-context-dependent")
        if index < len(e.a):
            arg = unwrap(e.a[index])
            if arg.op == ida_hexrays.cot_num:
                row.update(value=arg.numval(), status="constant-channel-observation")
            elif "-table" in channel and arg.op == ida_hexrays.cot_obj:
                count = 5 if channel.endswith("-five") else 8
                segment = ida_segment.getseg(arg.obj_ea)
                if segment and arg.obj_ea + count * 4 <= segment.end_ea:
                    values = [ida_bytes.get_dword(arg.obj_ea+i*4) for i in range(count)]
                    if count == 5: values = [values[i] for i in (0,1,2,2,3,4,3,4)]
                    row.update(tableAddress=hex(arg.obj_ea), rawHex=ida_bytes.get_bytes(arg.obj_ea, count*4).hex(),
                               values=values, status="static-state-table")
        self.rows.append(row)
        return 0


def export(output):
    digest = hashlib.sha256(Path(ida_nalt.get_input_file_path()).read_bytes()).hexdigest()
    if digest != "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028":
        raise ValueError("Wrong source sample")
    sites = []
    functions = set()
    for target, (channel, _) in TARGETS.items():
        for address in idautils.CodeRefsTo(target, False):
            function = ida_funcs.get_func(address)
            sites.append(dict(address=hex(address), target=hex(target), channel=channel,
                              function=hex(function.start_ea) if function else None))
            if function:
                functions.add(function.start_ea)
    rows, errors = [], []
    for address in sorted(functions):
        try:
            visitor = Calls()
            visitor.apply_to(ida_hexrays.decompile(address).body, None)
            rows.extend(dict(function=hex(address), functionName=ida_name.get_name(address), **r) for r in visitor.rows)
        except Exception as error:
            errors.append(dict(function=hex(address), reason=str(error)))
    found = {(r["address"], r["target"]) for r in rows}
    unresolved = [r for r in sites if (r["address"], r["target"]) not in found]
    report = dict(sha256=digest, targets={hex(k): v[0] for k,v in TARGETS.items()},
                  directCallSites=sites, observations=rows, unmatchedSites=unresolved, errors=errors,
                  boundary="All direct references to listed shared setters/layout entrypoints; indirect calls, receiver lifetime and dynamic table writes require contextual review. Individual channel observations are not confirmed complete combinations.")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(dict(sites=len(sites), functions=len(functions), observations=len(rows), unmatched=len(unresolved), errors=len(errors))))


if __name__ == "__main__":
    export(Path(__file__).resolve().parents[3] / "local/generated/research/text-styles/usage.json")
