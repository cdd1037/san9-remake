"""Checks for reference-free inference and the calibrated map features."""

import copy
import json
import unittest
from pathlib import Path

from calibrated_neighbors import FEATURES, classify, extract, fit_tree, predict, probability
from navigation import navigation


def plain_scenario():
    positions = [(11, 3, 12), (22, 35, 12), (3000, 19, 12)]
    return {
        "settlements": [{"id": city, "kind": 0, "x": x, "y": y} for city, x, y in positions],
        "military": {
            "width": 40, "height": 25, "terrainIds": [0] * 1000,
            "terrains": [{"id": 0, "landCost": 10, "waterCost": None, "isWater": False}],
            "blockedCells": [], "territoryExits": [],
            "geography": {"footprintScale": 5, "blockedCells": []},
            "formations": [
                {"id": 0, "movementBudget": 60, "footprint": {"width": 4, "height": 4},
                 "soldierFootprints": [{"minimumSoldiers": 6000, "footprint": {"width": 5, "height": 5}},
                                       {"minimumSoldiers": 20000, "footprint": {"width": 7, "height": 7}}]},
                {"id": 20, "movementBudget": 60, "footprint": {"width": 4, "height": 4}},
            ],
            "places": [{"id": city, "position": {"x": x * 5 + 2, "y": y * 5 + 2},
                        "footprint": {"width": 1, "height": 1}, "attack": {"range": 10}}
                       for city, x, y in positions],
        },
    }


def constant_model(limit=1.52):
    return {"landFormationId": 0, "navalFormationId": 20, "referenceSoldiers": 10000,
            "tree": {"probability": 1.0}, "bypassLimit": limit}


def validate(scenario, result, model):
    graph, cities, seeds, masks, scale, water, _ = navigation(
        scenario, model["landFormationId"], model["navalFormationId"], model["referenceSoldiers"])
    indices = {c["id"]: i for i, c in enumerate(cities)}
    assert len(result["pairs"]) == len(cities) * (len(cities) - 1) // 2
    expected, checked = set(), 0
    for row in result["pairs"]:
        assert classify(row, model) == row["decision"]
        if row["decision"] != "neighbor":
            continue
        a, b = [indices[c] for c in row["cities"]]
        nodes, forbidden = row["nodes"], ~((1 << a) | (1 << b))
        assert nodes[0] == seeds[a] and nodes[-1] == seeds[b]
        assert not any(masks[n] & forbidden for n in nodes)
        cost = sum(dict(graph[n])[other] for n, other in zip(nodes, nodes[1:]))
        assert row["days"] == cost / scale
        assert abs(row["days"] / row["minimumDays"] - row["bypassRatio"]) < 1e-12
        assert row["bypassRatio"] <= model["bypassLimit"]
        assert row["waterCells"] == sum(water[n] for n in nodes)
        expected.update((tuple(row["cities"]), tuple(row["cities"][::-1])))
        checked += 1
    assert expected == set(map(tuple, result["graph"]))
    return checked


class CalibratedNeighborTests(unittest.TestCase):
    def test_inference_does_not_read_reference_labels(self):
        rows = extract(plain_scenario())
        first = predict(rows, constant_model())
        changed = [dict(r, label=0, original=False) for r in rows]
        self.assertEqual(first["graph"], predict(changed, constant_model())["graph"])

    def test_training_learns_a_general_feature_threshold(self):
        rows = [{"features": [value] + [0] * (len(FEATURES) - 1), "label": label}
                for value, label in [(0, 0), (.1, 0), (.2, 0), (.3, 0), (1, 1), (1.1, 1), (1.2, 1), (1.3, 1)]]
        tree = fit_tree(rows)
        self.assertEqual(tree["feature"], "geographicLens")
        self.assertEqual(probability(tree, [.15] + [0] * 5), 0)
        self.assertEqual(probability(tree, [1.15] + [0] * 5), 1)
        with self.assertRaises(ValueError):
            fit_tree([])

    def test_relabeling_and_city_reordering_do_not_change_features_or_edges(self):
        s = plain_scenario()
        baseline = extract(s)
        mapping = {11: 91000, 22: 77, 3000: 6001}
        for place in s["settlements"] + s["military"]["places"]:
            place["id"] = mapping[place["id"]]
        s["settlements"].reverse()
        s["military"]["places"].reverse()
        changed = extract(s)
        original = {frozenset(mapping[c] for c in r["cities"]): (r["features"], r["bypassRatio"]) for r in baseline}
        actual = {frozenset(r["cities"]): (r["features"], r["bypassRatio"]) for r in changed}
        self.assertEqual(original, actual)

    def test_uniform_terrain_scaling_preserves_dimensionless_features(self):
        s = plain_scenario()
        original = extract(s)
        s["military"]["terrains"][0]["landCost"] *= 3
        changed = extract(s)
        for before, after in zip(original, changed):
            for a, b in zip(before["features"], after["features"]):
                self.assertAlmostEqual(a, b)
            self.assertEqual(before["bypassRatio"], after["bypassRatio"])

    def test_clearance_uses_all_equivalent_shortest_routes(self):
        s = plain_scenario()
        positions = [(11, 1, 1), (22, 7, 7), (3000, 4, 4)]
        s["military"].update(width=9, height=9, terrainIds=[0] * 81)
        for place, (_, x, y) in zip(s["settlements"], positions):
            place.update(x=x, y=y)
        for place, (_, x, y) in zip(s["military"]["places"], positions):
            place["position"] = {"x": x * 5 + 2, "y": y * 5 + 2}
        row = next(r for r in extract(s) if r["cities"] == [11, 22])
        # A middle-going shortest route touches C; an equally short outer L route stays three cells away.
        self.assertEqual(row["features"][FEATURES.index("blockerClearance")], 3 / 12)
        self.assertEqual(row["bypassRatio"], 1)

    def test_real_barrier_rejects_a_pair_even_when_model_votes_yes(self):
        s = plain_scenario()
        s["military"]["blockedCells"] = [{"x": 20, "y": y} for y in range(25)]
        result = predict(extract(s), constant_model())
        self.assertNotIn([11, 22], result["graph"])
        self.assertEqual(next(r for r in result["pairs"] if r["cities"] == [11, 22])["decision"], "unreachable")

    def test_costly_city_bypass_is_rejected_without_an_absolute_time_cap(self):
        rows = extract(plain_scenario())
        row = next(r for r in rows if r["cities"] == [11, 22])
        self.assertGreater(row["bypassRatio"], 1)
        self.assertEqual(classify(row, constant_model(limit=1)), "excessive-defense-detour")
        self.assertEqual(classify(row, constant_model(limit=1.52)), "neighbor")
        far = copy.deepcopy(row)
        far.update(minimumDays=row["minimumDays"] * 10, days=row["days"] * 10)
        self.assertGreater(far["days"], 60)
        self.assertEqual(classify(far, constant_model()), "neighbor")

    def test_complete_city_defense_barrier_is_not_a_direct_neighbor(self):
        s = plain_scenario()
        s["military"]["places"][2]["footprint"]["height"] = 25
        rows = extract(s)
        row = next(r for r in rows if r["cities"] == [11, 22])
        self.assertTrue(row["reachable"])
        self.assertEqual(classify(row, constant_model()), "third-city-defense-block")

    def test_cheap_city_chain_does_not_remove_a_qualified_bypass(self):
        s = plain_scenario()
        result = predict(extract(s), constant_model())
        self.assertIn([11, 22], result["graph"])
        self.assertEqual(validate(s, result, constant_model()), 3)

    def test_repeatability_and_duplicate_position_validation(self):
        s = plain_scenario()
        self.assertEqual(extract(s), extract(copy.deepcopy(s)))
        s["settlements"][1].update(x=s["settlements"][0]["x"], y=s["settlements"][0]["y"])
        with self.assertRaises(ValueError):
            extract(s)


if __name__ == "__main__":
    import sys
    if len(sys.argv) == 5 and sys.argv[1] == "--validate":
        scenario = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
        model = json.loads(Path(sys.argv[3]).read_text(encoding="utf-8"))
        result = json.loads(Path(sys.argv[4]).read_text(encoding="utf-8"))
        print(f"Validated {validate(scenario, result, model)} calibrated neighbor routes and decisions.")
    else:
        unittest.main()
