"""Behavioral acceptance for front ownership, route costs and ordinary/expedition scope."""

import copy
import json
import sys
import unittest
from pathlib import Path

from operational_neighbors import OperationalMap, PlanningPolicy, scenario_opponents
from test_frontier_neighbors import positioned


def world(positions):
    scenario = positioned([(city, x, y) for city, x, y, _ in positions])
    owners = {city: owner for city, _, _, owner in positions}
    for place in scenario["settlements"]:
        place.update(ownerId=owners[place["id"]], canHostPersons=True, durability=100)
    for place in scenario["military"]["places"]:
        place["attack"]["range"] = 0
    return scenario


def decision(result, source, target):
    return next(row for row in result["pairs"] if (row["source"], row["target"]) == (source, target))


class OperationalNeighborTests(unittest.TestCase):
    def test_front_base_should_launch_while_rear_base_supplies_it(self):
        scenario = world([(11, 3, 12, 1), (50, 25, 12, 1), (22, 35, 12, 2)])
        result = OperationalMap(scenario).evaluate(scenario["settlements"], 1, [2])
        self.assertEqual(decision(result, 11, 22)["decision"], "rear-base")
        self.assertEqual(decision(result, 50, 22)["decision"], "ordinary-front")

    def test_a_weak_far_target_does_not_enlarge_the_local_attack_window(self):
        scenario = world([(11, 3, 12, 1), (22, 10, 12, 2), (33, 35, 12, 2)])
        model = OperationalMap(scenario)
        scenario["settlements"][1]["soldiers"] = 100000
        scenario["settlements"][2]["soldiers"] = 1
        result = model.evaluate(scenario["settlements"], 1, [2])
        self.assertEqual(decision(result, 11, 22)["decision"], "ordinary-front")
        self.assertEqual(decision(result, 11, 33)["decision"], "beyond-local-front")
        self.assertEqual(decision(model.evaluate(scenario["settlements"], 1, [2], PlanningPolicy(10, 1000)), 11, 33)["decision"], "beyond-local-front")
        scenario["settlements"][1]["soldiers"], scenario["settlements"][2]["soldiers"] = 1, 100000
        self.assertEqual(result, model.evaluate(scenario["settlements"], 1, [2]))

    def test_an_enemy_near_the_route_is_not_automatically_a_required_conquest(self):
        scenario = world([(11, 3, 12, 1), (22, 11, 12, 2), (33, 7, 12, 2)])
        model = OperationalMap(scenario)
        row = decision(model.evaluate(scenario["settlements"], 1, [2]), 11, 22)
        self.assertEqual(row["decision"], "ordinary-front")
        self.assertGreater(row["safeDays"], row["directDays"])
        self.assertLessEqual(row["safeDays"] - row["directDays"], 10)

    def test_a_hostile_base_really_sealing_the_route_must_be_handled_first(self):
        scenario = world([(11, 3, 12, 1), (22, 35, 12, 2), (33, 19, 12, 2)])
        scenario["military"]["places"][2]["footprint"]["height"] = 25
        result = OperationalMap(scenario).evaluate(scenario["settlements"], 1, [2])
        self.assertEqual(decision(result, 11, 22)["decision"], "interposed-hostile-base")
        self.assertEqual(decision(result, 11, 33)["decision"], "ordinary-front")

    def test_a_long_bypass_is_not_ordinary_direct_access(self):
        scenario = world([(11, 3, 12, 1), (22, 35, 12, 2), (33, 19, 12, 2)])
        scenario["military"]["places"][2]["footprint"]["height"] = 17
        row = decision(OperationalMap(scenario).evaluate(scenario["settlements"], 1, [2]), 11, 22)
        self.assertEqual(row["decision"], "excessive-bypass")
        self.assertGreater(row["safeDays"] - row["directDays"], 10)

    def test_neutral_defenses_do_not_become_imaginary_hostile_territory(self):
        scenario = world([(11, 3, 12, 1), (22, 25, 12, 2), (33, 15, 12, 3)])
        scenario["military"]["places"][2]["attack"]["range"] = 10000
        row = decision(OperationalMap(scenario).evaluate(scenario["settlements"], 1, [2]), 11, 22)
        self.assertEqual(row["decision"], "ordinary-front")
        self.assertEqual(row["safeDays"], row["directDays"])

    def test_non_garrison_facilities_are_targets_and_blockers_but_not_launch_bases(self):
        scenario = world([(11, 3, 12, 1), (50, 6, 12, 1), (22, 35, 12, 2), (33, 19, 12, 2)])
        scenario["settlements"][1]["canHostPersons"] = False
        scenario["settlements"][3]["canHostPersons"] = False
        scenario["military"]["places"][3]["footprint"]["height"] = 25
        result = OperationalMap(scenario).evaluate(scenario["settlements"], 1, [2])
        self.assertNotIn(50, result["sourceBases"])
        self.assertIn(33, result["targetBases"])
        self.assertEqual(decision(result, 11, 22)["decision"], "interposed-hostile-base")
        self.assertEqual(decision(result, 11, 33)["decision"], "ordinary-front")

    def test_captured_port_changes_the_front_without_sharing_its_qualification_with_rear_city(self):
        scenario = world([(11, 3, 12, 1), (50, 20, 12, 2), (22, 35, 12, 2)])
        scenario["settlements"][1]["kind"] = 1
        scenario["regionCityIds"] = {"11": 11, "50": 11, "22": 22}
        model = OperationalMap(scenario)
        before = model.evaluate(scenario["settlements"], 1, [2])
        self.assertNotIn(50, before["sourceBases"])
        changed = copy.deepcopy(scenario["settlements"])
        changed[1]["ownerId"] = 1
        after = model.evaluate(changed, 1, [2])
        self.assertEqual(decision(after, 50, 22)["decision"], "ordinary-front")
        self.assertEqual(decision(after, 11, 22)["decision"], "rear-base")
        # The city projection is for display, not permission to pair every
        # source in city 11 with target 22. Keep the actual base pair witness.
        self.assertIn((11, 22), after["ordinaryCityEdges"])
        self.assertEqual(scenario["settlements"][1]["ownerId"], 2)

    def test_a_remote_front_is_recorded_as_expedition_instead_of_silently_lost(self):
        scenario = world([(11, 3, 12, 1), (22, 35, 12, 2)])
        scenario["military"]["terrains"][0]["landCost"] = 100
        result = OperationalMap(scenario).evaluate(scenario["settlements"], 1, [2])
        row = decision(result, 11, 22)
        self.assertEqual(row["decision"], "expedition-front")
        self.assertGreater(row["safeDays"], 30)
        self.assertEqual(result["ordinaryCityEdges"], [])
        self.assertEqual(result["expeditionCityEdges"], [(11, 22)])

    def test_explicit_policy_boundary_is_in_days(self):
        scenario = world([(11, 3, 12, 1), (22, 39, 12, 2)])
        model = OperationalMap(scenario)
        exact = decision(model.evaluate(scenario["settlements"], 1, [2]), 11, 22)
        self.assertEqual(exact["safeDays"], 30)
        self.assertEqual(exact["decision"], "ordinary-front")
        strict = decision(model.evaluate(scenario["settlements"], 1, [2], PlanningPolicy(10, 29)), 11, 22)
        self.assertEqual(strict["decision"], "expedition-front")

    def test_diplomatic_filter_matches_the_ordinary_ai_initial_state_rules(self):
        scenario = {"forces": [{"id": i} for i in range(1, 6)],
                    "alliance": {"memberForceIds": [1, 2], "targetForceId": 3},
                    "attackRequests": [{"requestedForceId": 1, "requesterForceId": 4}],
                    "information": {"relations": [{"firstId": 1, "secondId": 5, "value": 80}]}}
        self.assertEqual(scenario_opponents(scenario, 1), [3])

    def test_routes_replay_with_hostile_masks_and_do_not_depend_on_identity_order(self):
        scenario = world([(11, 3, 12, 1), (50, 25, 12, 1), (22, 35, 12, 2)])
        model = OperationalMap(scenario)
        result = model.evaluate(scenario["settlements"], 1, [2])
        for row in result["pairs"]:
            if row["route"] is None:
                continue
            target = model.indices[row["target"]]
            forbidden = sum(1 << model.indices[t] for t in result["targetBases"] if t != row["target"])
            nodes = row["route"]["nodes"]
            self.assertEqual((nodes[0], nodes[-1]), (model.seeds[model.indices[row["source"]]], model.seeds[target]))
            self.assertTrue(all(not model.defenses[n] & forbidden for n in nodes))
            self.assertEqual(row["route"]["cost"], sum(dict(model.graph[a])[b] for a, b in zip(nodes, nodes[1:])))
        changed = copy.deepcopy(scenario)
        ids = {11: 90011, 50: 4, 22: 600}
        for place in changed["settlements"] + changed["military"]["places"]:
            place["id"] = ids[place["id"]]
        changed["settlements"].reverse()
        changed["military"]["places"].reverse()
        other = OperationalMap(changed).evaluate(changed["settlements"], 1, [2])
        normalize = lambda rows, rename: {(rename[r["source"]], rename[r["target"]]): (r["decision"], r["safeDays"]) for r in rows}
        self.assertEqual(normalize(result["pairs"], ids), normalize(other["pairs"], {i: i for i in ids.values()}))


def validate_map(content, output, owner, captured):
    from time import perf_counter

    scenario = json.loads((Path(content) / "scenario.json").read_text(encoding="utf-8"))
    model = OperationalMap(scenario)
    enemies = scenario_opponents(scenario, owner)
    checks, sensitivity = [], []
    for name in ("initial", "captured-linzi"):
        state = copy.deepcopy(scenario["settlements"])
        if name != "initial":
            next(p for p in state if p["id"] == captured)["ownerId"] = owner
        saved = json.loads((Path(output) / name / "neighbors.json").read_text(encoding="utf-8"))
        started = perf_counter()
        repeated = model.evaluate(state, owner, enemies)
        elapsed = perf_counter() - started
        assert saved["pairs"] == repeated["pairs"]
        assert saved["ordinaryCityEdges"] == [list(pair) for pair in repeated["ordinaryCityEdges"]]
        checked = 0
        for row in repeated["pairs"]:
            if row["route"] is None:
                continue
            nodes = row["route"]["nodes"]
            forbidden = sum(1 << model.indices[t] for t in repeated["targetBases"] if t != row["target"])
            assert (nodes[0], nodes[-1]) == (model.seeds[model.indices[row["source"]]], model.seeds[model.indices[row["target"]]])
            assert all(not model.defenses[n] & forbidden for n in nodes)
            assert row["route"]["cost"] == sum(dict(model.graph[a])[b] for a, b in zip(nodes, nodes[1:]))
            assert row["safeDays"] == row["route"]["cost"] / model.scale
            if row["decision"] in ("ordinary-front", "expedition-front"):
                assert row["safeDays"] - row["directDays"] <= 10
                assert row["safeDays"] <= row["nearestEnemyDays"] + 10
                assert row["safeDays"] <= row["nearestFriendlyApproachDays"] + 10
                assert (row["safeDays"] <= 30) == (row["decision"] == "ordinary-front")
            checked += 1
        checks.append({"state": name, "routeWitnesses": checked, "deterministic": True, "warmQuerySeconds": elapsed})
        for horizon in (20, 30, 40):
            result = repeated if horizon == 30 else model.evaluate(state, owner, enemies, PlanningPolicy(10, horizon))
            sensitivity.append({"state": name, "ordinaryDays": horizon,
                                "ordinary": [[r["source"], r["target"], r["safeDays"]] for r in result["pairs"] if r["decision"] == "ordinary-front"],
                                "expedition": [[r["source"], r["target"], r["safeDays"]] for r in result["pairs"] if r["decision"] == "expedition-front"]})
    validation = {"unitTests": 12, "routeChecks": checks, "sensitivity": sensitivity,
                  "note": "Initial-state and controlled capture geometry only; not a production-AI deployment or campaign simulation."}
    (Path(output) / "verification.json").write_text(json.dumps(validation, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"unitTests": 12, "routeChecks": checks}, indent=2))


if __name__ == "__main__":
    if len(sys.argv) == 6 and sys.argv[1] == "--validate":
        validate_map(sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]))
    else:
        unittest.main()
