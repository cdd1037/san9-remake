"""Semantic checks for the two declared, untrained rules and their route witnesses."""

import copy
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

from frontier_neighbors import INF, MapExperiment, compare, nearest_cities, shared_fronts, shortest_route, unordered
from test_calibrated_neighbors import plain_scenario


def positioned(positions):
    scenario = plain_scenario()
    scenario["settlements"] = [{"id": city, "kind": 0, "x": x, "y": y} for city, x, y in positions]
    scenario["military"]["places"] = [{"id": city, "position": {"x": x * 5 + 2, "y": y * 5 + 2},
                                        "footprint": {"width": 1, "height": 1}, "attack": {"range": 10}}
                                       for city, x, y in positions]
    return scenario


class FrontierNeighborTests(unittest.TestCase):
    def test_an_intervening_city_separates_collinear_fronts(self):
        experiment = MapExperiment(plain_scenario())
        result = experiment.generate("frontier")
        self.assertEqual(unordered(result["graph"]), {(11, 3000), (22, 3000)})
        self.assertEqual(result["pairs"][0]["decision"], "no-shared-front")

    def test_tied_regions_retain_both_cities_instead_of_choosing_by_identity(self):
        owners, _ = nearest_cities([[0, 10, 20], [20, 10, 0]])
        self.assertEqual(owners, [1, 3, 2])
        self.assertIn((0, 1), shared_fronts([[(1, 10)], [(0, 10), (2, 10)], [(1, 10)]], owners))
        # Zero-width/point contact is deliberately retained, not broken by input order.
        self.assertIn((0, 1), shared_fronts([[]], [3]))

    def test_an_equal_cost_outer_route_can_keep_a_city_pair(self):
        experiment = MapExperiment(positioned([(11, 1, 1), (22, 7, 7), (3000, 4, 4)]))
        result = experiment.generate("frontier")
        self.assertIn((11, 22), unordered(result["graph"]))
        row = next(r for r in result["pairs"] if r["cities"] == [11, 22])
        self.assertEqual(row["detourRatio"], 1)

    def test_terrain_barriers_are_respected_before_any_neighborhood_rule(self):
        scenario = positioned([(11, 3, 12), (22, 35, 12)])
        scenario["military"]["blockedCells"] = [{"x": 20, "y": y} for y in range(25)]
        experiment = MapExperiment(scenario)
        for mode in ("frontier", "corridor"):
            result = experiment.generate(mode)
            self.assertEqual(result["graph"], [])
            self.assertEqual(result["pairs"][0]["decision"], "unreachable")

    def test_shared_front_alone_does_not_override_a_third_city_defense(self):
        scenario = positioned([(11, 3, 3), (22, 35, 3), (3000, 19, 20)])
        baseline = MapExperiment(scenario)
        self.assertIn((11, 22), unordered(baseline.generate()["graph"]))
        scenario["military"]["places"][2]["attack"]["range"] = 10000
        changed = MapExperiment(scenario)
        self.assertIn((0, 1), changed.fronts)
        row = next(r for r in changed.generate()["pairs"] if r["cities"] == [11, 22])
        self.assertEqual(row["decision"], "no-qualified-route")

    def test_renaming_reordering_and_cost_scaling_preserve_both_graphs(self):
        scenario = positioned([(11, 3, 3), (22, 35, 15), (3000, 19, 9)])
        baseline = MapExperiment(scenario)
        changed = copy.deepcopy(scenario)
        mapping = {11: 80001, 22: 17, 3000: 991}
        for place in changed["settlements"] + changed["military"]["places"]:
            place["id"] = mapping[place["id"]]
        changed["settlements"].reverse()
        changed["military"]["places"].reverse()
        changed["military"]["terrains"][0]["landCost"] *= 3
        other = MapExperiment(changed)
        for mode in ("frontier", "corridor"):
            expected = {tuple(sorted(mapping[c] for c in pair)) for pair in unordered(baseline.generate(mode)["graph"])}
            self.assertEqual(expected, unordered(other.generate(mode)["graph"]))

    def test_factor_two_is_a_real_rule_and_every_route_is_auditable(self):
        experiment = MapExperiment(positioned([(11, 3, 12), (22, 35, 12), (3000, 19, 20)]))
        result = experiment.generate("corridor")
        self.assertIn((11, 22), unordered(result["graph"]))
        for mode in ("frontier", "corridor"):
            for row in experiment.generate(mode)["pairs"]:
                if row["decision"] != "neighbor":
                    continue
                a, b = [next(i for i, c in enumerate(experiment.cities) if c["id"] == city) for city in row["cities"]]
                nodes = row["route"]["nodes"]
                self.assertEqual((nodes[0], nodes[-1]), (experiment.seeds[a], experiment.seeds[b]))
                self.assertTrue(all(experiment.permitted(a, b, mode)(node) for node in nodes))
                self.assertEqual(row["route"]["cost"], sum(dict(experiment.graph[u])[v] for u, v in zip(nodes, nodes[1:])))
                if mode == "corridor":
                    self.assertEqual(row["route"]["cost"], experiment.fields[a][experiment.seeds[b]])

    def test_no_reference_model_or_file_access_is_needed_for_generation(self):
        with patch("pathlib.Path.read_text", side_effect=AssertionError("Inference must not load a reference")):
            experiment = MapExperiment(plain_scenario())
            generated = experiment.generate()
        before = copy.deepcopy(generated)
        first = compare(generated, [[11, 22]], [])
        second = compare(generated, generated["graph"], generated["graph"])
        self.assertNotEqual(first, second)
        self.assertEqual(before, generated)

    def test_route_made_of_shortest_corridor_nodes_can_still_be_a_detour(self):
        graph = [[] for _ in range(10)]
        for a, b in [(0, 1), (0, 2), (1, 3), (2, 4), (3, 5), (4, 5), (5, 7), (7, 9), (3, 6), (6, 8), (8, 9)]:
            graph[a].append((b, 1))
            graph[b].append((a, 1))
        lower = [5, 4, 4, 3, 3, 2, 2, 1, 1, 0]
        allowed = lambda node: node not in (1, 7)
        # All nodes belong to some original shortest route, but the remaining
        # connection needs to go backward through a branch and takes seven steps.
        self.assertEqual(shortest_route(graph, 0, 9, lower, allowed)["cost"], 7)
        self.assertIsNone(shortest_route(graph, 0, 9, lower, allowed, maximum=5))


def validate_map(scenario_path, output):
    experiment = MapExperiment(json.loads(Path(scenario_path).read_text(encoding="utf-8")))
    indices = {c["id"]: i for i, c in enumerate(experiment.cities)}
    checks = []
    for mode in ("frontier", "corridor"):
        saved = json.loads((Path(output) / f"{mode}.json").read_text(encoding="utf-8"))
        repeated = experiment.generate(mode)
        assert saved["graph"] == repeated["graph"]
        assert [(r["cities"], r["decision"], r["route"]) for r in saved["pairs"]] == [
            (r["cities"], r["decision"], r["route"]) for r in repeated["pairs"]]
        checked = 0
        for row in saved["pairs"]:
            if row["decision"] != "neighbor":
                continue
            a, b = [indices[city] for city in row["cities"]]
            route, endpoints = row["route"], (1 << a) | (1 << b)
            nodes = route["nodes"]
            assert (nodes[0], nodes[-1]) == (experiment.seeds[a], experiment.seeds[b])
            assert all(not experiment.defenses[node] & ~endpoints for node in nodes)
            assert route["cost"] == sum(dict(experiment.graph[u])[v] for u, v in zip(nodes, nodes[1:]))
            assert row["days"] == route["cost"] / experiment.scale
            if mode == "frontier":
                assert all(experiment.nearest[node] & endpoints for node in nodes)
            else:
                assert route["cost"] == experiment.fields[a][experiment.seeds[b]]
                for node in nodes:
                    third = min((field[node] for c, field in enumerate(experiment.fields) if c not in (a, b)), default=INF)
                    assert third * 2 >= min(experiment.fields[a][node], experiment.fields[b][node])
            checked += 1
        checks.append({"mode": mode, "checkedRoutes": checked, "referenceFreeRegenerationEqual": True})
    result = {"unitTests": 9, "inputCities": len(experiment.cities), "routeChecks": checks}
    (Path(output) / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "--validate":
        validate_map(sys.argv[2], sys.argv[3])
    else:
        unittest.main()
