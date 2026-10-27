"""State-dependent operational fronts: approach from friendly bases, not city labels."""

import argparse
import hashlib
import heapq
import json
from array import array
from dataclasses import dataclass
from pathlib import Path
from time import perf_counter

from navigation import INF, navigation


@dataclass(frozen=True)
class PlanningPolicy:
    slack_days: int = 10
    ordinary_days: int = 30

    def __post_init__(self):
        if self.slack_days < 0 or self.ordinary_days <= 0:
            raise ValueError("Invalid planning policy.")


def active(place):
    return not place.get("isDormant", False) and not place.get("isDestroyed", False) and place.get("durability", 1) > 0


def scenario_opponents(scenario, owner):
    """Initial-state ordinary-AI diplomatic predicate; callers may instead supply explicit enemies."""
    forces = {f["id"]: f for f in scenario.get("forces", [])}
    alliance = scenario.get("alliance") or {}
    members = alliance.get("memberForceIds", [])
    requests = scenario.get("attackRequests", [])
    relations = {(r["firstId"], r["secondId"]): r["value"]
                 for r in scenario.get("information", {}).get("relations", [])}
    enemies = []
    for other, force in forces.items():
        if other == owner or force.get("isDormant") or force.get("isEliminated") or force.get("isBarbarian"):
            continue
        if owner in members:
            if other == alliance.get("targetForceId"):
                enemies.append(other)
                continue
            if other in members:
                continue
        if any({r["requestedForceId"], r["requesterForceId"]} == {owner, other} for r in requests):
            continue
        if relations.get(tuple(sorted((owner, other))), 50) // 20 < 3:
            enemies.append(other)
    return sorted(enemies)


def field(graph, seed, blocked=None, goals=None):
    """Reverse costs and forward next steps, optionally stopping when all queried bases settle."""
    costs, next_steps = array("q", [INF]) * len(graph), array("i", [-1]) * len(graph)
    if blocked is not None and blocked[seed]:
        return costs, next_steps
    costs[seed] = 0
    queue = [(0, seed)]
    remaining = None if goals is None else {n for n in goals if blocked is None or not blocked[n]}
    while queue:
        cost, node = heapq.heappop(queue)
        if cost != costs[node]:
            continue
        if remaining is not None:
            remaining.discard(node)
            if not remaining:
                break
        for other, weight in graph[node]:
            if blocked is not None and blocked[other]:
                continue
            candidate = cost + weight
            if candidate < costs[other]:
                costs[other], next_steps[other] = candidate, node
                heapq.heappush(queue, (candidate, other))
    return costs, next_steps


def path_to(start, end, next_steps):
    nodes = [start]
    while nodes[-1] != end:
        other = next_steps[nodes[-1]]
        if other < 0:
            raise AssertionError("Incomplete route witness.")
        nodes.append(other)
    return nodes


class OperationalMap:
    """Static terrain/geometry is shared; ownership and diplomatic inputs belong to each query."""

    def __init__(self, scenario, land=0, naval=20, soldiers=10000):
        self.scenario = scenario
        definitions = {p["id"] for p in scenario["military"]["places"]}
        sites = [p for p in scenario["settlements"] if p["id"] in definitions]
        self.graph, self.sites, self.seeds, self.defenses, self.scale, _, _ = navigation(
            scenario, land, naval, soldiers, sites=sites)
        self.indices = {p["id"]: i for i, p in enumerate(self.sites)}
        if len(self.indices) != len(self.sites):
            raise ValueError("Duplicate base identities.")
        self.clear_fields = {}

    def clear_field(self, source):
        if source not in self.clear_fields:
            self.clear_fields[source] = field(self.graph, self.seeds[source])[0]
        return self.clear_fields[source]

    def clear_route(self, source, target):
        costs = self.clear_field(source)
        node, end = self.seeds[target], self.seeds[source]
        if costs[node] >= INF:
            return []
        path = [node]
        while node != end:
            node = next(other for other, weight in self.graph[node] if costs[other] + weight == costs[node])
            path.append(node)
        return path[::-1]

    def evaluate(self, state, owner, enemies, policy=PlanningPolicy()):
        enemies = set(enemies) - {owner}
        world = {p["id"]: p for p in state}
        sources = [i for i, site in enumerate(self.sites) if active(world[site["id"]]) and
                   world[site["id"]].get("canHostPersons", True) and world[site["id"]].get("ownerId") == owner]
        targets = [i for i, site in enumerate(self.sites) if active(world[site["id"]]) and world[site["id"]].get("ownerId") in enemies]
        hostile = sum(1 << t for t in targets)
        slack = policy.slack_days * self.scale
        rows = []
        # All real hostile bases, including ports and gates, can interpose. Friendly
        # and neutral defenses do not become imaginary enemies merely by being near.
        for target in targets:
            forbidden = hostile & ~(1 << target)
            blocked = bytearray(bool(mask & forbidden) for mask in self.defenses)
            costs, next_steps = field(self.graph, self.seeds[target], blocked, [self.seeds[s] for s in sources])
            for source in sources:
                direct = self.clear_field(source)[self.seeds[target]]
                safe = costs[self.seeds[source]]
                row = {"source": self.sites[source]["id"], "target": self.sites[target]["id"],
                       "decision": "terrain-unreachable" if direct >= INF else "interposed-hostile-base",
                       "directDays": direct / self.scale if direct < INF else None, "safeDays": None,
                       "route": None}
                if safe < INF:
                    row["safeDays"] = safe / self.scale
                    if safe - direct > slack:
                        row["decision"] = "excessive-bypass"
                    else:
                        row["decision"] = "reachable"
                        row["route"] = {"cost": safe, "nodes": path_to(self.seeds[source], self.seeds[target], next_steps)}
                if direct < INF:
                    # Diagnostic only: a chosen unrestricted shortest route's first
                    # enemy fire zone is not proof that every route crosses that base.
                    first = next((self.defenses[n] & forbidden for n in self.clear_route(source, target)
                                  if self.defenses[n] & forbidden), 0)
                    row["firstEnemyFireZonesOnOneShortestRoute"] = [p["id"] for i, p in enumerate(self.sites) if first >> i & 1]
                rows.append(row)
        # Only feasible direct approaches establish a front; an enemy's blocked
        # shortcut must not make an actually usable friendly approach look too far.
        usable = [r for r in rows if r["decision"] == "reachable"]
        nearest_target, nearest_source = {}, {}
        for row in usable:
            source, target, cost = row["source"], row["target"], row["route"]["cost"]
            nearest_target[source] = min(nearest_target.get(source, INF), cost)
            nearest_source[target] = min(nearest_source.get(target, INF), cost)
        for row in usable:
            source, target, days = row["source"], row["target"], row["safeDays"]
            cost = row["route"]["cost"]
            row["nearestEnemyDays"] = nearest_target[source] / self.scale
            row["nearestFriendlyApproachDays"] = nearest_source[target] / self.scale
            if cost > nearest_source[target] + slack:
                row["decision"] = "rear-base"
            elif cost > nearest_target[source] + slack:
                row["decision"] = "beyond-local-front"
            else:
                row["decision"] = "ordinary-front" if cost <= policy.ordinary_days * self.scale else "expedition-front"
        city_ids = self.scenario.get("regionCityIds", {})
        city = lambda base: city_ids.get(str(base), base)
        city_edges = lambda decision: sorted({(city(r["source"]), city(r["target"])) for r in rows
                                              if r["decision"] == decision and city(r["source"]) != city(r["target"])})
        return {"owner": owner, "enemies": sorted(enemies), "policy": {"slackDays": policy.slack_days, "ordinaryDays": policy.ordinary_days},
                "sourceBases": [self.sites[s]["id"] for s in sources], "targetBases": [self.sites[t]["id"] for t in targets],
                "ordinaryCityEdges": city_edges("ordinary-front"), "expeditionCityEdges": city_edges("expedition-front"),
                "pairs": sorted(rows, key=lambda r: (r["source"], r["target"]))}


def report(result, names):
    lines = ["# 按实际战线生成的定向进攻范围", "", "此为几何与归属实验，未接入游戏，不承诺这些据点已满足兵力、人员、士气或外交命令资格。", "",
             "普通进攻：从合适的己方前沿据点出发；目标不比最近敌据点多走一旬；避开中间敌据点最多多花一旬；行军不超过三旬。",
             "超过三旬的前线机会单列为远征，不混入普通进攻。", "", "| 来源 | 目标 | 参考行军天数 | 相比不避敌多花 |", "| --- | --- | ---: | ---: |"]
    for row in result["pairs"]:
        if row["decision"] == "ordinary-front":
            lines.append(f"| {names[row['source']]} | {names[row['target']]} | {row['safeDays']:.1f} | {row['safeDays'] - row['directDays']:.1f} |")
    lines += ["", "## 远征候选", "", "| 来源 | 目标 | 参考行军天数 |", "| --- | --- | ---: |"]
    for row in result["pairs"]:
        if row["decision"] == "expedition-front":
            lines.append(f"| {names[row['source']]} | {names[row['target']]} | {row['safeDays']:.1f} |")
    lines += ["", "## 其他组合的判定", "", "| 来源 | 目标 | 安全参考天数 | 原因 |", "| --- | --- | ---: | --- |"]
    reasons = {"rear-base": "己方另有据点到目标快超过一旬，应从前沿组织", "beyond-local-front": "比该来源最近敌据点远超过一旬",
               "excessive-bypass": "绕避中间敌据点多花超过一旬", "interposed-hostile-base": "其他敌据点封住通道", "terrain-unreachable": "地形不可达"}
    for row in result["pairs"]:
        if row["decision"] in reasons:
            days = f"{row['safeDays']:.1f}" if row["safeDays"] is not None else "—"
            lines.append(f"| {names[row['source']]} | {names[row['target']]} | {days} | {reasons[row['decision']]} |")
    return "\n".join(lines) + "\n"


def main(args):
    root, output = Path(args.content), Path(args.output)
    scenario_path = root / "scenario.json"
    scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
    texts = json.loads((root / "messages/text.json").read_text(encoding="utf-8"))["entries"]
    started = perf_counter()
    model = OperationalMap(scenario)
    policy = PlanningPolicy(args.slack_days, args.ordinary_days)
    enemies = scenario_opponents(scenario, args.owner) if args.enemies is None else [int(x) for x in args.enemies.split(",") if x]
    state = [dict(p) for p in scenario["settlements"]]
    for capture in args.capture:
        place, owner = map(int, capture.split(":"))
        next(p for p in state if p["id"] == place)["ownerId"] = owner
    result = model.evaluate(state, args.owner, enemies, policy)
    elapsed = perf_counter() - started
    names = {p["id"]: texts[p["nameKey"]] for p in model.sites}
    result["baseNames"] = names
    result["manifest"] = {"inputSha256": hashlib.sha256(scenario_path.read_bytes()).hexdigest(),
                          "algorithmSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                          "navigationSha256": hashlib.sha256(Path(__file__).with_name("navigation.py").read_bytes()).hexdigest(),
                          "inferenceSeconds": elapsed, "captures": args.capture,
                          "referenceProfile": {"land": 0, "naval": 20, "soldiers": 10000}}
    output.mkdir(parents=True, exist_ok=True)
    (output / "neighbors.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "report.md").write_text(report(result, names), encoding="utf-8")
    counts = {decision: sum(r["decision"] == decision for r in result["pairs"])
              for decision in sorted({r["decision"] for r in result["pairs"]})}
    print(json.dumps({"owner": args.owner, "decisions": counts, "ordinaryDirectedCityPairs": len(result["ordinaryCityEdges"]),
                      "expeditionDirectedCityPairs": len(result["expeditionCityEdges"]), "seconds": round(elapsed, 3)}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--content", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--owner", type=int, default=0)
    parser.add_argument("--enemies")
    parser.add_argument("--capture", action="append", default=[])
    parser.add_argument("--slack-days", type=int, default=10)
    parser.add_argument("--ordinary-days", type=int, default=30)
    main(parser.parse_args())
