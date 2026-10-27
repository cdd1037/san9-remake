"""Map navigation and city-defense geometry for military-neighbor generation."""

import heapq
import math


INF = 10**12


def build_grid(scenario, mode, block_kind, directional, water_scale=1, sites=None):
    military = scenario["military"]
    width, height = military["width"], military["height"]
    size = width * height
    terrains = {t["id"]: t for t in military["terrains"]}
    costs = []
    for terrain_id in military["terrainIds"]:
        terrain = terrains[terrain_id]
        cost = terrain["waterCost"] if mode == "mixed" and terrain["isWater"] else terrain["landCost"]
        costs.append((cost or 0) * (water_scale if mode == "mixed" and terrain["isWater"] else 1))
    blocks = military["geography"]["blockedCells"] if block_kind == "geography" else military["blockedCells"]
    for point in blocks:
        costs[point["y"] * width + point["x"]] = 0
    cities = sorted(sites if sites is not None else
                    (s for s in scenario["settlements"] if s["kind"] == 0), key=lambda s: s["id"])
    seeds = [s["y"] * width + s["x"] for s in cities]
    # Endpoint relaxation permits leaving/entering authored city centers.
    for seed in seeds:
        if not costs[seed]:
            costs[seed] = 10
    exits = military["territoryExits"]
    neighbors = [[] for _ in range(size)]
    for index in range(size):
        if not costs[index]:
            continue
        x, y = index % width, index // width
        for dx, dy, bit in ((0, -1, 1), (1, 0, 2), (0, 1, 4), (-1, 0, 8)):
            xx, yy = x + dx, y + dy
            if 0 <= xx < width and 0 <= yy < height and (not directional or exits[index] & bit):
                destination = yy * width + xx
                if costs[destination]:
                    # Symmetric half-cell costs avoid charging one endpoint twice.
                    neighbors[index].append((destination, costs[index] + costs[destination]))
    return neighbors, cities, seeds


def distance_fields(neighbors, seeds):
    all_distances = []
    for seed in seeds:
        distances = [INF] * len(neighbors)
        distances[seed] = 0
        queue = [(0, seed)]
        while queue:
            distance, node = heapq.heappop(queue)
            if distance != distances[node]:
                continue
            for destination, weight in neighbors[node]:
                candidate = distance + weight
                if candidate < distances[destination]:
                    distances[destination] = candidate
                    heapq.heappush(queue, (candidate, destination))
        all_distances.append(distances)
    return all_distances


def soldier_shape(formation, soldiers):
    shape = formation["footprint"]
    for band in sorted(formation.get("soldierFootprints", []), key=lambda b: b["minimumSoldiers"]):
        if soldiers >= band["minimumSoldiers"]:
            shape = band["footprint"]
    return shape


def bounds(position, shape, scale=1):
    width, height = shape["width"] * scale, shape["height"] * scale
    left, top = position["x"] - width // 2, position["y"] - height // 2
    return left, top, left + width, top + height


def area_distance(a, b):
    return max(0, a[0] - b[2] + 1, b[0] - a[2] + 1) + max(0, a[1] - b[3] + 1, b[1] - a[3] + 1)


def defense_reach(place, military):
    attack = place.get("attack")
    if not attack:
        return 0
    facilities = {f["id"]: f for f in military.get("facilities", [])}
    tactics = {t["id"]: t for t in military.get("tactics", [])}
    facility = facilities.get(place.get("facilityTypeId"), {})
    enabled = set(place.get("enabledTacticIds", [])) | set(facility.get("tacticIds", []))
    modifiers = [tactics[t].get("facilityAttack") for t in enabled if t in tactics]
    modifiers.append(facility.get("innateAttack"))
    bonus = max((m["rangeBonus"] for m in modifiers if m), default=0)
    reach = attack["range"] + bonus + attack.get("maximumElevationBonus", 0)
    if any(m and m.get("usesRockfallPower") for m in modifiers):
        reach = max(reach, (military.get("rockfall") or {}).get("attackRange", 0))
    return reach


def navigation(scenario, land_id, naval_id, soldiers, sites=None):
    military = scenario["military"]
    graph, cities, seeds = build_grid(scenario, "mixed", "movement", False, sites=sites)
    formations = {f["id"]: f for f in military["formations"]}
    land, naval = formations[land_id], formations[naval_id]
    common = math.lcm(land["movementBudget"], naval["movementBudget"])
    scale = 2 * common
    terrain = {t["id"]: t for t in military["terrains"]}
    water = [terrain[t]["isWater"] for t in military["terrainIds"]]
    rates = []
    for terrain_id, on_water in zip(military["terrainIds"], water):
        rule, formation = terrain[terrain_id], naval if on_water else land
        cost = rule["waterCost"] if on_water else rule["landCost"]
        rates.append((cost or 10) * (common // formation["movementBudget"]))
    graph = [[(other, (rates[node] + rates[other]) * 5) for other, _ in row]
             for node, row in enumerate(graph)]
    width, height = military["width"], military["height"]
    land_shape, naval_shape = soldier_shape(land, soldiers), soldier_shape(naval, soldiers)
    # A node must accommodate the reference formation, not merely its center point.
    # Endpoint relaxation is inherited from the shared center-to-center probe.
    passable = [bool(row) for row in graph]
    for node, row in enumerate(graph):
        if not row:
            continue
        shape = naval_shape if water[node] else land_shape
        if max(shape["width"], shape["height"]) <= 5:
            continue  # Centered shapes fit wholly inside one coarse tile.
        area = bounds({"x": node % width * 5 + 2, "y": node // width * 5 + 2}, shape)
        if area[0] < 0 or area[1] < 0 or area[2] > width * 5 or area[3] > height * 5:
            passable[node] = False
            continue
        passable[node] = all(bool(graph[y * width + x])
                             for y in range(area[1] // 5, (area[3] - 1) // 5 + 1)
                             for x in range(area[0] // 5, (area[2] - 1) // 5 + 1))
    graph = [[(other, cost) for other, cost in row if passable[other]] if passable[node] else []
             for node, row in enumerate(graph)]
    places = {p["id"]: p for p in military["places"]}
    masks, reaches = [0] * len(graph), {}
    for index, city in enumerate(cities):
        place = places[city["id"]]
        reach = defense_reach(place, military)
        reaches[city["id"]] = reach
        area = bounds(place["position"], place["footprint"], military["geography"]["footprintScale"])
        padding = reach + max(land_shape["width"], land_shape["height"], naval_shape["width"], naval_shape["height"])
        for y in range(max(0, (area[1] - padding) // 5), min(height, (area[3] + padding + 4) // 5)):
            for x in range(max(0, (area[0] - padding) // 5), min(width, (area[2] + padding + 4) // 5)):
                node = y * width + x
                shape = naval_shape if water[node] else land_shape
                unit = bounds({"x": x * 5 + 2, "y": y * 5 + 2}, shape)
                if area_distance(area, unit) <= reach:
                    masks[node] |= 1 << index
    return graph, cities, seeds, masks, scale, water, reaches


def route_between(a, b, graph, seeds, masks, lower):
    start, end = seeds[a], seeds[b]
    forbidden = ~((1 << a) | (1 << b))
    if masks[start] & forbidden or masks[end] & forbidden:
        return None
    distances, parents = {start: 0}, {}
    queue = [(lower[start], 0, start)]
    while queue:
        _, cost, node = heapq.heappop(queue)
        if distances.get(node) != cost:
            continue
        if node == end:
            path = [end]
            while path[-1] != start:
                path.append(parents[path[-1]])
            return {"cost": cost, "nodes": path[::-1]}
        for other, weight in graph[node]:
            if masks[other] & forbidden:
                continue
            candidate = cost + weight
            if candidate < distances.get(other, INF):
                distances[other], parents[other] = candidate, node
                heapq.heappush(queue, (candidate + lower[other], candidate, other))
    return None
