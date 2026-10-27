"""Calibrate map-only military-neighbor rules, then generate without the reference table."""

import argparse
import json
from pathlib import Path

from navigation import INF, build_grid, distance_fields, navigation, route_between


FEATURES = ("geographicLens", "mixedLens", "euclideanLens", "minRank", "maxRank", "blockerClearance")


def extract(scenario, land_id=0, naval_id=20, soldiers=10000):
    graph, cities, seeds, masks, scale, water, _ = navigation(scenario, land_id, naval_id, soldiers)
    positions = [(c["x"], c["y"]) for c in cities]
    if len(set(positions)) != len(positions):
        raise ValueError("City positions must be distinct.")
    fields = distance_fields(graph, seeds)
    geographic, geographic_cities, geographic_seeds = build_grid(scenario, "mixed", "geography", False)
    assert [c["id"] for c in geographic_cities] == [c["id"] for c in cities]
    geo_fields = distance_fields(geographic, geographic_seeds)
    rows = []
    for a in range(len(cities)):
        for b in range(a + 1, len(cities)):
            fa, fb = fields[a], fields[b]
            distance = fa[seeds[b]]
            row = {"cities": [cities[a]["id"], cities[b]["id"]], "reachable": distance < INF}
            if distance >= INF:
                rows.append(row)
                continue
            candidates = [c for c in range(len(cities)) if c not in (a, b) and fa[seeds[c]] < INF and fb[seeds[c]] < INF]
            ax, ay = positions[a]
            bx, by = positions[b]
            squared = (ax - bx) ** 2 + (ay - by) ** 2
            geo_distance = geo_fields[a][seeds[b]]
            travel_scores = {c: (fa[seeds[c]] ** 2 + fb[seeds[c]] ** 2) / distance ** 2 for c in candidates}
            geometric_scores = {c: ((ax - positions[c][0]) ** 2 + (ay - positions[c][1]) ** 2 +
                                    (bx - positions[c][0]) ** 2 + (by - positions[c][1]) ** 2) / squared for c in candidates}
            geographic_scores = [(geo_fields[a][seeds[c]] ** 2 + geo_fields[b][seeds[c]] ** 2) / geo_distance ** 2
                                 for c in candidates]
            if candidates:
                blocker = min(candidates, key=lambda c: (travel_scores[c], positions[c]))
                fc = fields[blocker]
                # Find the widest-clearance route over the entire shortest-path DAG.
                # This is independent of an arbitrarily chosen equivalent shortest path.
                corridor = [n for n, (da, db) in enumerate(zip(fa, fb)) if da + db == distance]
                widest = {}
                for node in sorted(corridor, key=fa.__getitem__):
                    if node == seeds[a]:
                        widest[node] = fc[node]
                        continue
                    previous = max((widest.get(other, -1) for other, cost in graph[node]
                                    if fa[other] + cost == fa[node]), default=-1)
                    if previous >= 0:
                        widest[node] = min(previous, fc[node])
                clearance = widest[seeds[b]] / distance
                row["blockerCityId"] = cities[blocker]["id"]
            else:
                clearance = 1.0
                row["blockerCityId"] = None
            rank_a = sum(fa[seeds[c]] <= distance for c in range(len(cities)) if c != a)
            rank_b = sum(fb[seeds[c]] <= distance for c in range(len(cities)) if c != b)
            # No competing city in the connected component means no lens exclusion.
            row["features"] = [min(geographic_scores, default=1e12), min(travel_scores.values(), default=1e12),
                               min(geometric_scores.values(), default=1e12), min(rank_a, rank_b), max(rank_a, rank_b), clearance]
            safe = route_between(a, b, graph, seeds, masks, fields[b])
            row.update(minimumDays=distance / scale, bypassRatio=safe["cost"] / distance if safe else None,
                       days=safe["cost"] / scale if safe else None, nodes=safe["nodes"] if safe else None,
                       waterCells=sum(water[n] for n in safe["nodes"]) if safe else None)
            rows.append(row)
    return rows


def fit_tree(rows, depth=6, min_leaf=4):
    if not rows:
        raise ValueError("Calibration requires training pairs.")
    n, positives = len(rows), sum(r["label"] for r in rows)
    node = {"sampleCount": n, "positiveCount": positives, "probability": positives / n}
    if depth == 0 or positives in (0, n):
        return node
    base, best = positives * (n - positives) / n, None
    for feature in range(len(FEATURES)):
        ordered = sorted(rows, key=lambda r: r["features"][feature])
        left_positive = 0
        for count in range(1, n):
            left_positive += ordered[count - 1]["label"]
            left_value, right_value = ordered[count - 1]["features"][feature], ordered[count]["features"][feature]
            if count < min_leaf or n - count < min_leaf or left_value == right_value:
                continue
            right_positive = positives - left_positive
            loss = left_positive * (count - left_positive) / count + right_positive * (n - count - right_positive) / (n - count)
            candidate = (loss, feature, (left_value + right_value) / 2)
            if best is None or candidate < best:
                best = candidate
    if best is None or best[0] >= base:
        return node
    _, feature, threshold = best
    node.update(feature=FEATURES[feature], threshold=threshold,
                left=fit_tree([r for r in rows if r["features"][feature] <= threshold], depth - 1, min_leaf),
                right=fit_tree([r for r in rows if r["features"][feature] > threshold], depth - 1, min_leaf))
    return node


def probability(tree, values):
    while "feature" in tree:
        index = FEATURES.index(tree["feature"])
        tree = tree["left"] if values[index] <= tree["threshold"] else tree["right"]
    return tree["probability"]


def tree_trace(tree, values):
    trace = []
    while "feature" in tree:
        value = values[FEATURES.index(tree["feature"])]
        left = value <= tree["threshold"]
        trace.append({"feature": tree["feature"], "value": value, "threshold": tree["threshold"], "left": left})
        tree = tree["left"] if left else tree["right"]
    return trace


def metrics(predicted, reference):
    return {"groups": len(predicted), "shared": len(predicted & reference), "additional": len(predicted - reference),
            "omitted": len(reference - predicted), "f1": 2 * len(predicted & reference) / max(1, len(predicted) + len(reference))}


def fit_bypass(rows, tree):
    values = sorted({r["bypassRatio"] for r in rows if r["bypassRatio"] is not None and probability(tree, r["features"]) >= .5})
    if not values:
        return 1.0
    candidates = [(a + b) / 2 for a, b in zip(values, values[1:])] + [values[-1] + 1e-6]
    reference = {tuple(r["cities"]) for r in rows if r["label"]}
    def loss(threshold):
        predicted = {tuple(r["cities"]) for r in rows if probability(tree, r["features"]) >= .5 and
                     r["bypassRatio"] is not None and r["bypassRatio"] <= threshold}
        return len(predicted ^ reference), len(reference - predicted), threshold
    return min(candidates, key=loss)


def classify(row, model):
    if not row["reachable"]:
        return "unreachable"
    if probability(model["tree"], row["features"]) < .5:
        return "not-local-neighbor"
    if row["bypassRatio"] is None:
        return "third-city-defense-block"
    if row["bypassRatio"] > model["bypassLimit"]:
        return "excessive-defense-detour"
    return "neighbor"


def predict(rows, model):
    output, graph = [], []
    for row in rows:
        result = dict(row, decision=classify(row, model))
        if row["reachable"]:
            result["ruleTrace"] = tree_trace(model["tree"], row["features"])
        if result["decision"] == "neighbor":
            graph.extend((row["cities"], row["cities"][::-1]))
        output.append(result)
    return {"features": FEATURES, "graph": sorted(graph), "pairs": output}


def cross_validate(rows, depth, fold_by_city):
    votes = {}
    for fold in range(5):
        training = [r for r in rows if all(fold_by_city[c] != fold for c in r["cities"])]
        tree = fit_tree(training, depth)
        limit = fit_bypass(training, tree)
        for row in rows:
            if any(fold_by_city[c] == fold for c in row["cities"]):
                value = probability(tree, row["features"]) if row["bypassRatio"] is not None and row["bypassRatio"] <= limit else 0
                votes.setdefault(tuple(row["cities"]), []).append(value)
    predicted = {pair for pair, values in votes.items() if sum(values) / len(values) >= .5}
    return metrics(predicted, {tuple(r["cities"]) for r in rows if r["label"]})


def calibrate(rows, reference_edges, cities, land_id=0, naval_id=20, soldiers=10000):
    reference = {tuple(sorted(p)) for p in reference_edges}
    training = [dict(r, label=int(tuple(r["cities"]) in reference)) for r in rows if r["reachable"]]
    if not training:
        raise ValueError("Calibration requires reachable city pairs.")
    tree = fit_tree(training)
    limit = fit_bypass(training, tree)
    model = {"schemaVersion": 1, "features": FEATURES, "maxDepth": 6, "minimumLeafSamples": 4,
             "landFormationId": land_id, "navalFormationId": naval_id, "referenceSoldiers": soldiers,
             "tree": tree, "bypassLimit": limit}
    selected = {tuple(r["cities"]) for r in training if classify(r, model) == "neighbor"}
    spatial = {c["id"]: index % 5 for index, c in enumerate(sorted(cities, key=lambda c: (c["x"], c["y"])))}
    identity = {c["id"]: c["id"] % 5 for c in cities}
    holdouts = {"identityFold": cross_validate(training, 6, identity),
                "spatialFold": cross_validate(training, 6, spatial)}
    return model, {"fit": metrics(selected, reference), "cityHoldouts": holdouts}


def compare(result, reference_edges):
    reference = {tuple(sorted(e)) for e in reference_edges}
    selected = {tuple(r["cities"]) for r in result["pairs"] if r["decision"] == "neighbor"}
    result["comparison"] = metrics(selected, reference)
    result["differences"] = {"additional": [], "omitted": [], "direction": []}
    for row in result["pairs"]:
        pair = tuple(row["cities"])
        if pair in selected - reference:
            result["differences"]["additional"].append(row)
        elif pair in reference - selected:
            result["differences"]["omitted"].append(row)
        if pair in selected & reference and ((pair in reference_edges) != (pair[::-1] in reference_edges)):
            result["differences"]["direction"].append(row)


def report(result, model):
    names = result["cityNames"]
    def title(row):
        return "—".join(names[c] for c in row["cities"])
    lines = ["# 按原版校准后的自动军事邻接", "", "生成阶段只读地图和已校准参数，不读取原版关系表。",
             "无60天上限，无指定城市对补边。参考部队配置保存在模型中。", "",
             f"结果：{len(result['graph']) // 2}组。",
             f"比较：{result.get('comparison', {})}。", "",
             f"校准得到的最大城防绕行比例：{model['bypassLimit']:.6f}。", ""]
    for key, heading in (("additional", "新增"), ("omitted", "原版有、未生成"), ("direction", "方向差异")):
        lines += [f"## {heading}", "", "| 城市对 | 最短参考天数 | 避让后参考天数 | 最强第三城竞争者 | 绕行比例 |",
                  "| --- | ---: | ---: | --- | ---: |"]
        records = result.get("differences", {}).get(key, [])
        if not records:
            lines += ["| 无 | — | — | — | — |"]
        for row in records:
            blocker = row.get("blockerCityId")
            before = f"{row['minimumDays']:.1f}" if row.get("minimumDays") is not None else "不可达"
            after = f"{row['days']:.1f}" if row.get("days") is not None else "不可达"
            ratio = f"{row['bypassRatio']:.3f}" if row.get("bypassRatio") is not None else "—"
            lines.append(f"| {title(row)} | {before} | {after} | {names.get(blocker, '无')} | {ratio} |")
        lines += [""]
    lines += ["## 全部邻接", "", "| 城市对 | 参考天数 |", "| --- | ---: |"]
    lines += [f"| {title(r)} | {r['days']:.1f} |" for r in result["pairs"] if r["decision"] == "neighbor"]
    return "\n".join(lines) + "\n"


def main(args):
    root = Path(args.content)
    scenario = json.loads((root / "scenario.json").read_text(encoding="utf-8"))
    reference = None
    if args.command == "calibrate":
        rows = extract(scenario, args.land, args.naval, args.soldiers)
        reference = set(map(tuple, json.loads(Path(args.reference).read_text(encoding="utf-8"))["edges"]))
        cities = [c for c in scenario["settlements"] if c["kind"] == 0]
        model, validation = calibrate(rows, reference, cities, args.land, args.naval, args.soldiers)
    else:
        model = json.loads(Path(args.model).read_text(encoding="utf-8"))
        if model["schemaVersion"] != 1 or list(model["features"]) != list(FEATURES):
            raise ValueError("Unsupported calibration model.")
        rows = extract(scenario, model["landFormationId"], model["navalFormationId"], model["referenceSoldiers"])
        validation = None
    result = predict(rows, model)
    texts = json.loads((root / "messages/text.json").read_text(encoding="utf-8"))["entries"]
    result["cityNames"] = {c["id"]: texts[c["nameKey"]] for c in scenario["settlements"] if c["kind"] == 0}
    # For generation, the optional reference is read only after prediction is complete.
    if reference is None and args.reference:
        reference = set(map(tuple, json.loads(Path(args.reference).read_text(encoding="utf-8"))["edges"]))
    if reference is not None:
        compare(result, reference)
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    if validation is not None:
        (output / "model.json").write_text(json.dumps(model, ensure_ascii=False, indent=2), encoding="utf-8")
        (output / "validation.json").write_text(json.dumps(validation, indent=2), encoding="utf-8")
    (output / "neighbors.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "comparison.md").write_text(report(result, model), encoding="utf-8")
    print("Neighbor groups:", len(result["graph"]) // 2)
    print("Comparison:", result.get("comparison", "reference not read"))
    if validation is not None:
        print("City holdouts:", validation["cityHoldouts"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("calibrate", "generate"))
    parser.add_argument("--content", required=True)
    parser.add_argument("--reference")
    parser.add_argument("--model")
    parser.add_argument("--output", required=True)
    parser.add_argument("--land", type=int, default=0)
    parser.add_argument("--naval", type=int, default=20)
    parser.add_argument("--soldiers", type=int, default=10000)
    args = parser.parse_args()
    if args.command == "calibrate" and not args.reference:
        parser.error("calibrate requires --reference")
    if args.command == "generate" and not args.model:
        parser.error("generate requires --model")
    main(args)
