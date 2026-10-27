"""Untrained military-neighbor experiments: shared fronts and direct corridors."""

import argparse
import hashlib
import heapq
import json
from itertools import combinations
from pathlib import Path
from time import perf_counter

from navigation import INF, distance_fields, navigation, route_between


def shortest_route(graph, start, end, lower, allowed, maximum=None):
    """Minimum-cost route in an explicitly allowed subgraph, with an exact lower bound."""
    if not allowed(start) or not allowed(end):
        return None
    costs, parents = {start: 0}, {}
    queue = [(lower[start], 0, start)]
    while queue:
        _, cost, node = heapq.heappop(queue)
        if costs.get(node) != cost:
            continue
        if node == end:
            path = [end]
            while path[-1] != start:
                path.append(parents[path[-1]])
            return {"cost": cost, "nodes": path[::-1]}
        for other, weight in graph[node]:
            if not allowed(other):
                continue
            candidate = cost + weight
            if maximum is not None and candidate + lower[other] > maximum:
                continue
            if candidate < costs.get(other, INF):
                costs[other], parents[other] = candidate, node
                heapq.heappush(queue, (candidate + lower[other], candidate, other))
    return None


def nearest_cities(fields):
    """All tied closest cities are retained; a city ID never breaks a spatial tie."""
    nearest, best_three = [], []
    for values in zip(*fields):
        minimum = min(values)
        nearest.append(sum(1 << city for city, value in enumerate(values) if value == minimum)
                       if minimum < INF else 0)
        best_three.append(sorted((value, city) for city, value in enumerate(values))[:3])
    return nearest, best_three


def shared_fronts(graph, nearest):
    """Two closest-city regions meet at a node or along a traversable edge."""
    fronts = {}
    for node, row in enumerate(graph):
        owners = [city for city in range(nearest[node].bit_length()) if nearest[node] >> city & 1]
        for pair in combinations(owners, 2):
            fronts.setdefault(pair, [node, node])
        for other, _ in row:
            if node >= other:
                continue
            others = [city for city in range(nearest[other].bit_length()) if nearest[other] >> city & 1]
            for a in owners:
                for b in others:
                    if a != b:
                        fronts.setdefault(tuple(sorted((a, b))), [node, other])
    return fronts


class MapExperiment:
    """Authored map inputs only. Original links and the old fitted model are not inputs."""

    def __init__(self, scenario, land=0, naval=20, soldiers=10000):
        self.graph, self.cities, self.seeds, self.defenses, self.scale, _, _ = navigation(
            scenario, land, naval, soldiers)
        if not self.cities:
            raise ValueError("At least one city is required.")
        ids = [c["id"] for c in self.cities]
        if len(set(ids)) != len(ids):
            raise ValueError("City identities must be distinct.")
        self.width = scenario["military"]["width"]
        self.fields = distance_fields(self.graph, self.seeds)
        self.nearest, self.best_three = nearest_cities(self.fields)
        self.fronts = shared_fronts(self.graph, self.nearest)

    def permitted(self, a, b, mode):
        endpoints = (1 << a) | (1 << b)
        forbidden = ~endpoints
        if mode == "frontier":
            return lambda node: bool(self.nearest[node] & endpoints) and not self.defenses[node] & forbidden
        if mode != "corridor":
            raise ValueError("Unknown experiment mode.")
        direct = self.fields[a][self.seeds[b]]

        def allowed(node):
            if self.defenses[node] & forbidden or self.fields[a][node] + self.fields[b][node] != direct:
                return False
            competitor = next((distance for distance, city in self.best_three[node] if city not in (a, b)), INF)
            # One declared strategic assumption, not a fitted threshold: a third city
            # dominates only if it reaches a route point in less than half either side's time.
            return competitor * 2 >= min(self.fields[a][node], self.fields[b][node])
        return allowed

    def generate(self, mode="frontier"):
        rows, edges = [], []
        for a, b in combinations(range(len(self.cities)), 2):
            pair = [self.cities[a]["id"], self.cities[b]["id"]]
            direct = self.fields[a][self.seeds[b]]
            row = {"cities": pair, "decision": "unreachable", "minimumDays": None, "route": None}
            if direct < INF:
                row["minimumDays"] = direct / self.scale
                if mode == "frontier" and (a, b) not in self.fronts:
                    row["decision"] = "no-shared-front"
                else:
                    route = shortest_route(self.graph, self.seeds[a], self.seeds[b], self.fields[b], self.permitted(a, b, mode),
                                           maximum=direct if mode == "corridor" else None)
                    row["decision"] = "neighbor" if route else "no-qualified-route"
                    row["route"] = route
                    if route:
                        row["days"] = route["cost"] / self.scale
                        row["detourRatio"] = route["cost"] / direct if direct else 1
                        edges.extend((pair, pair[::-1]))
                if (a, b) in self.fronts:
                    row["contact"] = self.fronts[a, b]
            rows.append(row)
        return {"mode": mode, "graph": sorted(edges), "pairs": rows}

    def evidence(self, row):
        """An excluded pair's ordinary shortest safe route illustrates intervening regions.

        This chosen route is a witness, not a claim that every physical route crosses these cities.
        The classification itself checks the complete allowed subgraph/all shortest routes.
        """
        indices = {c["id"]: i for i, c in enumerate(self.cities)}
        a, b = [indices[c] for c in row["cities"]]
        route = route_between(a, b, self.graph, self.seeds, self.defenses, self.fields[b])
        if route is None:
            return {"safeRoute": None}
        runs = []
        for node in route["nodes"]:
            owners = [c["id"] for i, c in enumerate(self.cities) if self.nearest[node] >> i & 1]
            if not runs or runs[-1]["cities"] != owners:
                runs.append({"cities": owners, "node": node})
        return {"safeRoute": route, "safeDays": route["cost"] / self.scale, "nearestCityRuns": runs}


def unordered(edges):
    return {tuple(sorted(pair)) for pair in edges}


def compare(result, reference_edges, previous_edges):
    selected, reference, previous = [unordered(edges) for edges in (result["graph"], reference_edges, previous_edges)]
    return {"groups": len(selected), "kept": len(selected & reference), "added": sorted(selected - reference),
            "omitted": sorted(reference - selected), "addedToPrevious": sorted(selected - previous),
            "removedFromPrevious": sorted(previous - selected),
            "newDirections": sorted(set(map(tuple, result["graph"])) - set(map(tuple, reference_edges)))}


def report(results, names):
    title = lambda pair: "—".join(names[c] for c in pair)
    lines = ["# 简单规则军事邻接实验", "", "生成不读取原表、旧模型或城市对例外；原表和旧边集仅在生成结束后对照。", "",
             "| 方法 | 生成组数 | 保留原版 | 额外 | 未保留 |", "| --- | ---: | ---: | ---: | ---: |"]
    for result in results:
        comparison = result["comparison"]
        lines.append(f"| {result['mode']} | {comparison['groups']} | {comparison['kept']} | {len(comparison['added'])} | {len(comparison['omitted'])} |")
    for result in results:
        lines += ["", f"## {result['mode']}", ""]
        for key, heading in (("added", "原版没有、实验新增"), ("omitted", "原版有、实验未保留"),
                             ("addedToPrevious", "相比当前112组新增"), ("removedFromPrevious", "相比当前112组移除")):
            lines += [f"### {heading}", "", "| 城市对 | 最短参考天数 | 实验路线天数 | 判定 |", "| --- | ---: | ---: | --- |"]
            index = {tuple(row["cities"]): row for row in result["pairs"]}
            for pair in result["comparison"][key]:
                row = index[tuple(pair)]
                before = f"{row['minimumDays']:.1f}" if row["minimumDays"] is not None else "不可达"
                after = f"{row['days']:.1f}" if row.get("days") is not None else "—"
                reason = {"neighbor": "共同前线与安全路线成立" if result["mode"] == "frontier" else "有合格最短通道",
                          "no-shared-front": "两城最近到达范围不相接", "no-qualified-route": "完整允许子图中无路线",
                          "unreachable": "地形不可达"}[row["decision"]]
                lines.append(f"| {title(pair)} | {before} | {after} | {reason} |")
            lines += [""]
        lines += ["### 未保留关系的路线证据", "", "下列是普通最短安全路线经过的最近城市范围，可能有其他等价或绕行路线；不能据此宣称第三城必经。", ""]
        for pair in result["comparison"]["omitted"]:
            row = index[tuple(pair)]
            intervening = []
            for run in row["evidence"].get("nearestCityRuns", []):
                if not set(run["cities"]) & set(pair):
                    for city in run["cities"]:
                        if city not in intervening:
                            intervening.append(city)
            if row["evidence"]["safeRoute"] is None:
                detail = "没有避开第三城城防的路线"
            elif intervening:
                detail = "一条最短安全路线进入了" + "、".join(names[c] for c in intervening) + "的最近到达范围"
            else:
                detail = "普通安全路线存在；限制后的完整路线集合不通过检查"
            lines.append(f"- {title(pair)}：{detail}。")
    return "\n".join(lines) + "\n"


def main(args):
    root, output = Path(args.content), Path(args.output)
    scenario_path = root / "scenario.json"
    scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
    started = perf_counter()
    experiment = MapExperiment(scenario, args.land, args.naval, args.soldiers)
    initialized = perf_counter()
    modes = ("frontier", "corridor") if args.mode == "both" else (args.mode,)
    results = [experiment.generate(mode) for mode in modes]
    generated = perf_counter()
    # Evaluation inputs are deliberately opened only after both graphs are complete.
    reference = json.loads(Path(args.reference).read_text(encoding="utf-8"))["edges"] if args.reference else []
    previous = json.loads(Path(args.previous).read_text(encoding="utf-8"))["graph"] if args.previous else []
    texts = json.loads((root / "messages/text.json").read_text(encoding="utf-8"))["entries"]
    names = {c["id"]: texts[c["nameKey"]] for c in experiment.cities}
    output.mkdir(parents=True, exist_ok=True)
    for result in results:
        if args.reference:
            result["comparison"] = compare(result, reference, previous)
            changed = set(result["comparison"]["omitted"])
            for row in result["pairs"]:
                if tuple(row["cities"]) in changed:
                    row["evidence"] = experiment.evidence(row)
        result["cityNames"] = names
        (output / f"{result['mode']}.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        if args.reference:
            c = result["comparison"]
            print(f"{result['mode']}: groups={c['groups']}, kept={c['kept']}, added={len(c['added'])}, omitted={len(c['omitted'])}")
        else:
            print(f"{result['mode']}: groups={len(result['graph']) // 2}; reference not read")
    if args.reference:
        (output / "comparison.md").write_text(report(results, names), encoding="utf-8")
    manifest = {"inputSha256": hashlib.sha256(scenario_path.read_bytes()).hexdigest(),
                "algorithmSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                "navigationSha256": hashlib.sha256(Path(__file__).with_name("navigation.py").read_bytes()).hexdigest(),
                "referenceProfile": {"landFormationId": args.land, "navalFormationId": args.naval, "soldiers": args.soldiers},
                "cities": len(experiment.cities), "width": scenario["military"]["width"], "height": scenario["military"]["height"],
                "initializationSeconds": initialized - started, "generationSeconds": generated - initialized,
                "inferenceSeconds": generated - started, "modes": modes}
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"InferenceSeconds={generated - started:.3f}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--content", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--mode", choices=("frontier", "corridor", "both"), default="both")
    parser.add_argument("--reference")
    parser.add_argument("--previous")
    parser.add_argument("--land", type=int, default=0)
    parser.add_argument("--naval", type=int, default=20)
    parser.add_argument("--soldiers", type=int, default=10000)
    main(parser.parse_args())
