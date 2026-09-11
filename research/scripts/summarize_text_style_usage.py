"""Summarize IDA style evidence without inventing combinations across object lifetimes."""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path


def summarize(data):
    tables = defaultdict(list)
    constants = Counter()
    dynamic = []
    for row in data["observations"]:
        if row["status"] == "static-state-table":
            tables[(row["channel"].removesuffix("-five"), tuple(row["values"]))].append(row)
        elif row["status"] == "constant-channel-observation":
            constants[(row["channel"], row["value"])] += 1
        else:
            dynamic.append(row)
    return {
        "sha256": data["sha256"],
        "boundary": data["boundary"],
        "directCallSites": len(data["directCallSites"]),
        "functions": len({r["function"] for r in data["directCallSites"] if r["function"]}),
        "observations": len(data["observations"]),
        "stateTables": [dict(channel=channel, values=list(values), evidence=rows)
                        for (channel, values), rows in sorted(tables.items())],
        "constantChannels": [dict(channel=channel, value=value, occurrences=count)
                             for (channel, value), count in sorted(constants.items())],
        "dynamicObservations": dynamic,
        "unmatchedSites": data["unmatchedSites"],
        "errors": data["errors"],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    args = parser.parse_args()
    data = summarize(json.loads(args.input.read_text(encoding="utf-8")))
    output = args.input.with_name("catalog.json")
    output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    lines = ["# 原作文字样式调用审计", "", f"样本 SHA-256：`{data['sha256']}`。", "",
             f"直接调用 {data['directCallSites']} 处，函数 {data['functions']} 个，反编译观察 {data['observations']} 条。",
             "", "以下为实际引用的单通道状态表，**不是已确认的完整样式组合**。5 项表按原作映射展开为 8 状态。",
             "状态顺序：普通、悬停、按下、按下悬停、禁用、禁用悬停、禁用按下、禁用按下悬停。",
             "颜色为原作调色板号，-1 表示关闭该效果。不能将不同接收对象的前景和效果表交叉组合。", "",
             "| 通道 | 八状态值 | 表地址 | 调用次数 |", "|---|---|---|---|"]
    for table in data["stateTables"]:
        values = ", ".join(str(-1 if v == 0xffffffff else v) for v in table["values"])
        addresses = ", ".join(sorted({r["tableAddress"] for r in table["evidence"]}))
        lines.append(f"| {table['channel']} | {values} | {addresses} | {len(table['evidence'])} |")
    lines += ["", "## 未闭合边界", "",
              f"动态或上下文相关观察 {len(data['dynamicObservations'])} 条；未匹配直接调用 {len(data['unmatchedSites'])} 处；反编译错误 {len(data['errors'])} 条。",
              "接收对象别名、跨函数修改、直接字段写入、间接调用仍需人工核对。详细定位保留在 catalog.json 和 usage.json。", ""]
    output.with_suffix(".md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"tables": len(data["stateTables"]), "dynamic": len(data["dynamicObservations"]),
                      "unmatched": len(data["unmatchedSites"])}))


if __name__ == "__main__":
    main()
