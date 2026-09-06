"""Independent private-sample audit for BEH-UI-0001 / RE-SAN9PK-0157."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from PIL import Image


# Independent source mappings; groups describe presentation responsibilities, not file layout.
# Each tuple is (role, resource, columns, palette_alpha).
ASSET_GROUPS = {
    "catalog": ("G_Common.s9", 0, 200, [
        *[("catalog-frame-"+p,r,1,True) for p,r in [("top-left",314),("top",315),("top-right",316),("bottom-left",320),("bottom",321),("bottom-right",322)]],
        *[("catalog-header-"+p,332+i,4,True) for i,p in enumerate(["left","middle","right"])],
        *[("catalog-row-"+p,293+i,4,True) for i,p in enumerate(["left","middle","right"])],
        ("catalog-sort-down",335,1,True),("catalog-sort-up",336,1,True),("catalog-tab",380,6,True),
        ("catalog-portrait-frame",297,1,True),("catalog-help",354,4,True),
        ("catalog-scroll-down",372,4,True),("catalog-scroll-up",373,4,True),
        *[("catalog-scroll-"+p,374+i,1,True) for i,p in enumerate(["track-top","track-middle","track-bottom","thumb-top","thumb-middle","thumb-bottom"])],
        *[("catalog-tabs-"+p,364+i,1,True) for i,p in enumerate(["top-left","top","top-right","left","right","bottom-left","bottom","bottom-right"])]]),
    "information-menu": ("G_Main.s9", 5975, 60, [
        ("information-menu-" + role, 5975 + 32 + i, 1, False)
        for i, role in enumerate(["top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"])]),
    "information-menu-buttons": ("G_Main.s9", 5975, 60, [("information-menu-button-" + role, 5998+i, 4, False) for i,role in enumerate(["left", "middle", "right"])]),
    "information": ("G_Common.s9", 0, 200, [
        ("information-background", 337, 1, False),
        *[("information-frame-" + role, resource, 1, True) for role, resource in [("bottom",338),("left",339),("right",340),("top",341),("bottom-left",342),("top-left",343),("bottom-right",344),("top-right",345),("ornament",346)]],
        *[("information-title-" + role, 361+i, 1, True) for i, role in enumerate(["left","middle","right"])],
        *[("information-footer-" + role, 382+i, 1, True) for i, role in enumerate(["left","middle","right"])],
        *[("information-action-" + role, 350+i, 4, False) for i, role in enumerate(["left","middle","right","close"])]]),
    "overview": ("G_MMap.s9", 999, 295, [
        ("overview-background", 1156, 1, False), ("overview-terrain", 1163, 1, False),
        ("overview-borders", 1162, 1, True), ("overview-ranking", 1160, 1, True),
        ("button-overview-forces", 1158, 4, True), ("button-overview-facilities", 1159, 4, True),
        ("button-overview-units", 1157, 4, True),
        *[(f"overview-city-{i}", 1161, 1, True) for i in range(50)],
        *[(f"overview-marker-{i}", 1050+i, 1, False) for i in range(51)],
        *[(f"overview-facility-{i}", 999+i, 1, False) for i in range(51)],
        *[(f"overview-unit-{i}", 1170+i, 1, False) for i in range(50)]]),
    "scrollbar": ("G_Main.s9", 5975, 60, [
        ("roster-scroll-decoration", 6060, 1, False),
        ("button-scroll-up", 6026, 4, False), ("button-scroll-down", 6027, 4, False),
        ("scroll-track-top", 6028, 1, False), ("scroll-track-middle", 6029, 1, False),
        ("scroll-track-bottom", 6030, 1, False), ("scroll-thumb-top", 6031, 1, False),
        ("scroll-thumb-middle", 6032, 1, False), ("scroll-thumb-bottom", 6033, 1, False)]),
    "hud": ("G_Main.s9", 5975, 60, [
        ("field-left", 6066, 1, False), ("field-middle", 6067, 1, False), ("field-right", 6068, 1, False),
        ("ruler-frame", 6069, 1, False), ("region-frame", 6072, 1, True),
        ("top-frame", 6063, 1, False), ("radar-frame", 6057, 1, False), ("sidebar-frame", 6059, 1, False),
        ("season-spring", 6039, 1, False), ("season-summer", 6040, 1, False),
        ("season-autumn", 6041, 1, False), ("season-winter", 6042, 1, False),
        ("button-territory", 6043, 6, False), ("button-facilities", 6044, 4, False),
        ("button-officers", 6045, 4, False), ("button-units", 6046, 4, False),
        ("button-information", 6049, 4, False), ("button-functions", 6048, 4, False),
        ("button-help", 6050, 4, False), ("button-map-mode", 6061, 4, False), ("button-advance", 6062, 4, False)]),
    "roster": ("G_Common.s9", 0, 200, [
        ("city-prefix", 43, 1, False), ("roster-row-left", 290, 4, True),
        ("roster-row-middle", 291, 4, True), ("roster-row-right", 292, 4, True)]),
    "messages": ("G_Common.s9", 0, 200, [
        ("message-left", 165, 1, True), ("message-middle", 166, 1, True), ("message-right", 167, 1, True)]),
    "bars": ("G_Common.s9", 0, 200, [
        ("bar-morale-frame", 390, 1, False), ("bar-durability-frame", 391, 1, False),
        ("bar-morale-normal", 392, 1, False), ("bar-durability-normal", 393, 1, False),
        ("bar-morale-critical", 394, 1, False), ("bar-morale-warning", 395, 1, False),
        ("bar-durability-critical", 396, 1, False), ("bar-durability-warning", 397, 1, False)]),
    "icons": ("G_Marker.s9", 1220, 311, [
        ("icon-emperor", 1243, 1, False), ("icon-training", 1244, 1, False), ("icon-merchant", 1245, 1, False)]),
}
SCOPES = (*ASSET_GROUPS, "minimap", "scenario", "information-art", "integrity")
ARCHIVE_SHAPES = {"G_Main.s9": (21, 98), "G_Common.s9": (67, 491), "G_Marker.s9": (5, 34), "G_MMap.s9": (16, 221)}


def expected_roles(groups):
    roles = []
    for group in groups:
        for name, _, columns, _ in ASSET_GROUPS[group][3]:
            roles.extend(name if frame == 0 else f"{name}-{frame}" for frame in range(columns))
    assert len(roles) == len(set(roles)), "Duplicate roles in independent audit catalog"
    return set(roles)


def verify_role_coverage(skin, groups, extra_roles=()):
    expected = expected_roles(groups)
    missing = expected - skin["roles"].keys()
    assert not missing, f"Missing selected roles: {sorted(missing)}"
    if set(groups) == set(ASSET_GROUPS):
        unknown = skin["roles"].keys() - expected - set(extra_roles)
        assert not unknown, f"Roles without independent source coverage: {sorted(unknown)}"


def verify_asset_hash(root, asset):
    assert hashlib.sha256((root / asset["path"]).read_bytes()).hexdigest().upper() == asset["sha256"], asset["path"]


def verify_skin_assets(root, data, skin, bindings, groups, extra_roles=()):
    verify_role_coverage(skin, groups, extra_roles)
    assets = {a["id"]: a for a in skin["assets"]}
    archives, checked = {}, {}
    for group in groups:
        filename, base, palette_base, records = ASSET_GROUPS[group]
        if filename not in archives:
            archives[filename] = parse_combined((data / filename).read_bytes())
            assert tuple(map(len, archives[filename])) == ARCHIVE_SHAPES[filename], filename
        palettes, images = archives[filename]
        checked[group] = 0
        for name, resource, columns, alpha in records:
            if name.startswith("roster-row-"):
                assert bindings[resource] == 234
            for frame in range(columns):
                role = name if frame == 0 else f"{name}-{frame}"
                asset = assets[skin["roles"][role]]
                verify_asset_hash(root, asset)
                with Image.open(root / asset["path"]) as actual:
                    try:
                        if name.startswith("overview-city-"):
                            city = int(name.removeprefix("overview-city-"))
                            w, h, indices = images[resource-base]
                            assert actual.mode == "RGBA" and actual.size == (w, h)
                            assert actual.tobytes() == bytes(v for index in indices for v in (255,255,255,143 if index == city+1 else 0))
                        else:
                            palette = palettes[bindings[resource]-palette_base]
                            if name == "overview-borders":
                                palette = bytearray(palette)
                                palette[52*4+3] = 72
                            verify_frame(actual, images[resource-base], palette, columns, frame, alpha)
                        if name.startswith("roster-row-") and frame in (0, 3):
                            assert not any(actual.getchannel("A").tobytes())
                    except AssertionError as error:
                        raise AssertionError(f"Source pixels mismatch: {role}") from error
                checked[group] += 1
        if group == "roster":
            assert skin["colors"]["roster-available"] == "F0F0F0" and skin["colors"]["roster-unavailable"] == "988058"
        if group == "overview":
            for color in range(51):
                assert skin["colors"][f"overview-force-{color}"] == palettes[8][(color+1)*4:(color+1)*4+3].hex().upper()
    return checked


def parse_combined(data):
    if len(data) < 4:
        raise ValueError("Missing palette count")
    count = struct.unpack_from("<I", data)[0]
    start = 4 + count * 1064
    if start + 4 > len(data) or struct.unpack_from("<I", data, start)[0] != len(data) - start:
        raise ValueError("Invalid image stream bounds")
    palettes = [data[44 + i * 1064:1068 + i * 1064] for i in range(count)]
    images = []
    position = start + 4
    while position < len(data):
        if position + 16 > len(data):
            raise ValueError("Truncated image header")
        size, stride, width, height = struct.unpack_from("<4I", data, position)
        if width == 0 or height == 0 or stride != width or size != 16 + stride * height or position + size > len(data):
            raise ValueError("Invalid indexed image")
        images.append((width, height, data[position + 16:position + size]))
        position += size
    return palettes, images


def verify_frame(actual, source, palette, columns, frame, palette_alpha=False):
    width, height, pixels = source
    assert width % columns == 0
    w = width // columns
    assert actual.mode == "RGBA" and actual.size == (w, height)
    rgba = actual.tobytes()
    for y in range(height):
        for x in range(w):
            index = pixels[y * width + frame * w + x]
            entry = palette[index * 4:index * 4 + 4]
            a = (y * w + x) * 4
            visible = entry != palette[:4]
            expected_alpha = (min(entry[3], 128) * 255 + 64) // 128 if palette_alpha else 255
            expected_alpha = expected_alpha if visible else 0
            assert rgba[a + 3] == expected_alpha
            # The exporter extends neighboring RGB into every fully transparent pixel.
            if expected_alpha > 0:
                assert rgba[a:a + 3] == entry[:3]


def verify_minimap(actual, squares, palette):
    """Independent pixel reference from the IDA rectangle/triangle picker (RE-0158)."""
    assert actual.mode == "RGBA" and actual.size == (320, 160)
    families = [0,6,1,2,1,2,3,1,2,7,4,5,7,6,6,4,4]
    for y in range(160):
        span = 4 * (min(y, 160-y)-1)
        left = max(0, (320-span)//2)
        for x in range(320):
            if not left <= x < left+span:
                assert actual.getpixel((x,y))[3] == 0
                continue
            wx, wy = x*40, y*40
            column, row = wx//32, wy//16
            dx, dy = wx%32, wy%16
            if (column+row)%2:
                if dx > 2*dy: column += 1
                else: row += 1
            elif dx+2*dy > 32:
                column += 1
                row += 1
            sx,sy = (row-2+column-200)//2, (row-2-column+200)//2
            color = 4*(families[squares[sy*200+sx]["terrain"]]+1)
            expected = (palette[color]&248,palette[color+1]&252,palette[color+2]&248,255)
            assert actual.getpixel((x,y)) == expected, (x,y,sx,sy)


def verify_officers(scenario, entries, texts, messages):
    """Independent raw-record reference: RE-0141 / RE-0158, BEH-UI-0002."""
    from square_map.scenario import parse_scenario_sections
    from parse_pk_text import decode_ls11, split_indexed_records, strip_common_terminator
    sections = parse_scenario_sections(scenario)
    names = split_indexed_records(decode_ls11(messages)[0])[2]
    def name(index):
        return strip_common_terminator(names[index])[0].decode("big5hkscs").replace("\U00020547", "伷").replace("\u92DB", "璝").replace("\u42B5", "儁")
    people = sections["prsn"]
    items = list(struct.iter_unpack("<11H", sections["item"]))
    offices = list(struct.iter_unpack("<I7H", sections["shak"]))
    expected_ids = []
    for index in range(800):
        raw = people[index*134:(index+1)*134]
        u16 = lambda offset: struct.unpack_from("<H", raw, offset)[0]
        if u16(0x1c) > 5:
            continue
        expected_ids.append(index)
        entry = next(e for e in entries if e["state"]["id"] == index)
        state = entry["state"]
        assert (state["settlementId"] if state["settlementId"] is not None else state["regionId"]) == u16(0x18)
        assert state["status"] == u16(0x1c)
        assert state["birthYear"] == u16(0x14) + 130
        assert state["officeId"] == (u16(0x26) if u16(0x26) < 76 else None)
        assert state["acted"] == bool(struct.unpack_from("<I", raw)[0] & 0x1000)
        assert state["injury"] == u16(0x2a)
        assert state["tacticProficiencies"] == list(struct.unpack_from("<9H", raw, 0x38))
        learned = int.from_bytes(raw[0x80:0x86], "little")
        assert state["learnedTactics"] == [i for i in range(41) if learned & (1 << i)]
        assert state["personality"] == u16(0x36)
        assert state["isFemale"] == bool(struct.unpack_from("<I", raw)[0] & 1)
        assert state["parentId"] == (u16(0x2e) if u16(0x2e) < 1031 else None)
        assert state["serviceStartYear"] == (u16(0x16) + 130 if u16(0x16) else None)
        source = name(1678 + 4*u16(6)) + name(1679 + 4*u16(8))
        assert texts[entry["nameKey"]]["sourceText"] == source
        for ability, offset, categories, bit in [("leadership",0x1e,[2],0),("war",0x20,[1],1),
                ("intelligence",0x22,[5,9],2),("politics",0x24,[3,4,6],3)]:
            assert state["baseAbilities"][ability] == u16(offset)
            bonus = max([i[6] for i in items if i[8] == index and i[9] != 0 and i[4] in categories], default=0)
            assert state["itemBonuses"][ability] == bonus
            rank = u16(0x26)
            office_bonus = offices[rank][7] if rank < 76 and offices[rank][6] & (1 << bit) else 0
            assert state["officeBonuses"][ability] == office_bonus
    assert [e["state"]["id"] for e in entries] == expected_ids
    for index, display in [(175,"孔伷"),(239,"朱俊"),(602,"刘𪻺")]:
        assert texts[f"officer.{index}"]["text"] == display



def verify_information_art(content, data, root):
    from square_map.scenario import parse_scenario_sections
    from square_map.graphics import parse_image_stream
    raw = parse_scenario_sections((data / "D_Sce002.S9").read_bytes())
    faces = parse_image_stream(data / "G_FaceL.s9")
    catalog_faces = parse_image_stream(data / "G_FaceS.s9")
    face_palette = (data / "P_Face.s9").read_bytes()[44:1068]
    palettes, still = parse_combined((data / "G_Still.s9").read_bytes())
    assets = {a["id"]: a for a in content["skin"]["assets"]}
    def check(role, width, height, pixels, palette):
        asset = assets[content["skin"]["roles"][role]]
        verify_asset_hash(root, asset)
        expected = Image.frombytes("P", (width, height), pixels)
        expected.putpalette(bytes(v for i in range(256) for v in palette[4*i:4*i+3]))
        with Image.open(root / asset["path"]) as actual:
            assert actual.mode == "RGBA" and actual.size == expected.size
            assert actual.convert("RGB").tobytes() == expected.convert("RGB").tobytes(), role
            assert actual.getchannel("A").getextrema() == (255,255), role
    for officer in content["scenario"]["officers"]:
        i = officer["state"]["id"]
        index = struct.unpack_from("<H", raw["prsn"], i*134+14)[0]
        image = faces[index]
        check(f"information-person-{i}", image.width, image.height, image.pixels, face_palette)
        image = catalog_faces[index]
        assert (image.width, image.height) == (64, 80)
        check(f"catalog-person-{i}", image.width, image.height, image.pixels, face_palette)
    for treasure in content["scenario"]["information"]["treasures"]:
        i = treasure["id"]
        index = struct.unpack_from("<H", raw["item"], i*22+6)[0] + 172
        check(f"information-treasure-{i}", *still[index], palettes[index])

def verify_scenario(content, data):
    package, p, text = content["map"], content["scenario"], content["text"]["entries"]
    from parse_pk_text import decode_ls11, split_indexed_records, strip_common_terminator
    messages = split_indexed_records(decode_ls11((data / "M_MsgPK.s9").read_bytes())[0])[2]
    hints = {"catalog-officers":8159, "information":8261, "functions":8260, "help":8262, "pause":8439,
             "territory":8430, "map-large":8291, "map-small":8293, "advance":8253,
             "facilities":8290, "officers":8289, "units":8288,
             "overview-forces":8440, "overview-facilities":8442, "overview-units":8441,
             **dict(zip(["information-"+r for r in ["ranks","forces","corps","cities","regions","units","officers","treasures","records","chronicle","units-empty","treasures-empty"]], [8287,8281,8366,8280,8367,8282,8279,8283,8405,8537,8371,8372]))}
    for role, index in hints.items():
        assert text[f"ui.hint.{role}"]["sourceText"] == strip_common_terminator(messages[index])[0].decode("big5hkscs")
    font = (data / "P_Font.s9").read_bytes()
    for style, rgb in [(2,"f06850"),(7,"f0f0f0")]:
        offset = (style // 2 * 32 + 16 + style % 2 * 8 + 7) * 4
        assert font[offset:offset+3].hex() == rgb
    from square_map.scenario import parse_scenario_sections
    raw = parse_scenario_sections((data / "D_Sce002.S9").read_bytes())
    for force in p["forces"]:
        flags = struct.unpack_from("<I",raw["forc"],force["id"]*86+8)[0]
        title = struct.unpack_from("<H",raw["forc"],force["id"]*86+16)[0]
        assert force["isBarbarian"] == (bool(flags & 4) or 4 <= title <= 8)
    info = p["information"]
    for force in p["forces"]:
        rank = struct.unpack_from("<H", raw["forc"], force["id"]*86+26)[0]
        assert force["rankId"] == (rank if rank < 8 else None)
        tactic_bits = int.from_bytes(raw["forc"][force["id"]*86+30:force["id"]*86+36], "little")
        assert force["providedTactics"] == [i for i in range(41) if tactic_bits & (1 << i)]
    assert [c["id"] for c in info["corps"]] == sorted({s["corpsId"] for s in p["settlements"] if s["corpsId"] is not None})
    for corps in info["corps"]:
        offset = corps["id"]*86
        owner, _, leader = struct.unpack_from("<3H", raw["forc"], offset+14)
        assert corps["ownerId"] == (corps["id"] if owner == 65535 else owner)
        assert corps["leaderId"] == leader
        if corps["ownerId"] == corps["id"]:
            assert corps["treasury"] is None
        else:
            assert (corps["treasury"]["money"], corps["treasury"]["food"]) == struct.unpack_from("<II", raw["forc"], offset)
    pairs = {(a["id"], b["id"]) for a in p["forces"] for b in p["forces"] if a["id"] < b["id"]}
    assert {(r["firstId"], r["secondId"]) for r in info["relations"]} == pairs
    for relation in info["relations"]:
        a, b = relation["firstId"], relation["secondId"]
        assert relation["value"] == raw["forc"][a*86+36+b] == raw["forc"][b*86+36+a]
    assert [r["id"] for r in info["ranks"]] == list(range(8))
    for r in info["ranks"]:
        record = struct.unpack_from("<I5H", raw["kans"], r["id"]*14)
        assert (r["commandCapacity"],r["loyaltyBonus"],r["requiredTrust"]) == (record[0],record[4],record[5])
    def original(index): return strip_common_terminator(messages[index])[0].decode("big5hkscs")
    for records, base, stride in [(info["ranks"],1652,2),(info["offices"],5065,2),(info["treasures"],1392,2)]:
        for r in records: assert text[r["nameKey"]]["sourceText"] == original(base + stride*r["id"])
    for city in p["settlements"]:
        if city["id"] < 50:
            assert (city["population"],city["draftPopulation"]) == struct.unpack_from("<II",raw["city"],city["id"]*56+4)
    visible = {o["state"]["id"] for o in p["officers"]}
    expected_items = [i for i in range(120) if struct.unpack_from("<HH",raw["item"],i*22+16)[0] in visible and struct.unpack_from("<H",raw["item"],i*22+18)[0] == 2]
    assert [t["id"] for t in info["treasures"]] == expected_items
    for t in info["treasureStates"]:
        assert t["discovered"] and t["holderId"] == struct.unpack_from("<H",raw["item"],t["id"]*22+16)[0]
    assert p["units"] == [] and all(struct.unpack_from("<H",raw["unit"],i*40+18)[0] == 0 for i in range(800))
    assert all(s["kind"] == (0 if s["id"] < 50 else 1) for s in p["settlements"])
    assert sum(s["id"] < 50 for s in p["settlements"]) == 50
    assert all("cityMarkerColor" not in s and "hasAvailableOfficer" not in s for s in p["settlements"])
    assert len([a for a in package["assets"] if a["id"].startswith("building-")]) == 624
    assert len(p["settlements"]) == 94
    player = next(f for f in p["forces"] if f["id"] == p["playerForceId"])
    officers = [o["state"] for o in p["officers"]]
    assert len(officers) == 210 and sum(o["status"] <= 3 for o in officers) == 195
    verify_officers((data / "D_Sce002.S9").read_bytes(), p["officers"], text, (data / "M_MsgPK.s9").read_bytes())
    assert all(o["nameKey"] in text for o in p["officers"])
    ruler = next(o for o in officers if o["id"] == player["leaderId"])
    assert ruler["ownerId"] == 5 and ruler["settlementId"] == 15
    facilities = [s for s in p["settlements"] if s["corpsId"] == 5 and s["isFacilityListEntry"]]
    assert all(s["ownerId"] == 5 for s in facilities)
    assert len(facilities) == 13 and next(s for s in facilities if s["id"] == 107)["isFacilityListEntry"]
    assert [s["id"] for s in facilities if s["id"] < 50] == [15,16,17,18]
    assert (text[player["nameKey"]]["text"], player["money"], player["food"], player["trust"]) == ("董卓", 9520, 73200, 100)
    for id, name, soldiers in [(15, "洛阳", 32000), (99, "虎牢关", 30000), (100, "孟津港", 1000), (107, "郿城塞", 3000)]:
        s = next(s for s in p["settlements"] if s["id"] == id)
        assert (text[s["nameKey"]]["text"], s["soldiers"], s["ownerId"], s["morale"]) == (name, soldiers, 5, 50)
    settlements = {s["id"]:s for s in p["settlements"]}
    assert settlements[15]["icons"] == ["merchant","emperor"] and settlements[17]["icons"] == ["training"]
    assert [s["id"] for s in p["settlements"] if s["ownerId"] == 5 and any(o["settlementId"] == s["id"] and o["ownerId"] == 5 and not o["acted"] for o in officers)] == [15,16,17,18,99]
    assert all(not s["icons"] for s in p["settlements"] if s["id"] >= 50)
    assert p["regionCityIds"]["99"] == 15
    assert [s["id"] for s in p["settlements"] if s["ownerId"] == 5] == [15, 16, 17, 18, 95, 96, 97, 98, 99, 100, 104, 106, 107]
    assert not {"M_MsgPK.s9", "G_Main.s9"}.intersection(a["name"] for a in package["sources"])


def verify_baseline(package, baseline):
    map_assets = {a["id"]: a for a in package["assets"]}
    old = json.loads((baseline / "map.json").read_text(encoding="utf-8"))
    assert old["schemaVersion"] == 8, "Baseline must use the current map contract"
    for key in ["buildingTypes", "squares", "terrain", "bridges", "buildings", "compositions", "clouds", "leftEdge", "rightEdge", "animations", "seasons"]:
        if key == "seasons":
            current = json.loads(json.dumps(package[key]))
            for season in current:
                for asset in season["textures"]:
                    asset["path"] = asset["path"].removeprefix("images/map/")
            assert old[key] == current, key
        else:
            assert old[key] == package[key], key
    for asset in old["assets"]:
        assert asset == {**map_assets[asset["id"]], "path": map_assets[asset["id"]]["path"].removeprefix("images/map/")}, asset["id"]


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("data", type=Path)
    parser.add_argument("package", type=Path)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--scope", action="append", choices=SCOPES,
                           help="Audit one responsibility; repeat to combine scopes")
    selection.add_argument("--all", action="store_true", help="Full source, pixel, data and integrity audit")
    parser.add_argument("--baseline", type=Path, help="Historical map comparison; requires --all")
    args = parser.parse_args(argv)
    if args.baseline and not args.all:
        parser.error("--baseline requires --all")
    return args


def main(argv=None):
    args = parse_args(argv)
    scopes = SCOPES if args.all else tuple(dict.fromkeys(args.scope))
    exe = args.exe.read_bytes()
    assert hashlib.sha256(exe).hexdigest() == "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
    content = json.loads((args.package / "content.json").read_text(encoding="utf-8"))
    package, p, skin = content["map"], content["scenario"], content["skin"]
    assert content["schemaVersion"] == 10
    assert package["schemaVersion"] == 8 and "presentation" not in package
    assert p["schemaVersion"] == 8 and skin["schemaVersion"] == 10
    assert not {a["id"] for a in skin["assets"]}.intersection(a["id"] for a in package["assets"])
    groups = [scope for scope in scopes if scope in ASSET_GROUPS]
    checked = {}
    if groups:
        bindings = {}
        for start, count in [(0x24ea90, 1425), (0x2601d0, 1145)]:
            for i in range(count):
                resource, palette = struct.unpack_from("<2I", exe, start + 8 * i)
                bindings[resource & 65535] = palette
        extra_roles = [f"{prefix}-person-{o['state']['id']}" for o in p["officers"] for prefix in ["information", "catalog"]] + [f"information-treasure-{t['id']}" for t in p["information"]["treasures"]]
        checked = verify_skin_assets(args.package, args.data, skin, bindings, groups, extra_roles)
    if "minimap" in scopes:
        asset = next(a for a in package["assets"] if a["id"] == "minimap-terrain")
        verify_asset_hash(args.package, asset)
        with Image.open(args.package / asset["path"]) as mini:
            verify_minimap(mini, package["squares"], (args.data / "P_MapEtc.s9").read_bytes()[44:1068])
        assert (asset["width"], asset["height"]) == (320, 160)
        assert len(package["boundaries"]) > 0
    if "scenario" in scopes:
        verify_scenario(content, args.data)
    if "information-art" in scopes:
        verify_information_art(content, args.data, args.package)
    if "integrity" in scopes:
        for asset in package["assets"] + [a for season in package["seasons"] for a in season["textures"]] + skin["assets"]:
            verify_asset_hash(args.package, asset)
    if args.baseline:
        verify_baseline(package, args.baseline)
    for group, count in checked.items():
        print(f"ASSET_AUDIT_OK scope={group} images={count} checks=source-rgb,alpha,output-hash")
    for scope in scopes:
        if scope not in ASSET_GROUPS:
            print(f"DATA_AUDIT_OK scope={scope}")
    print(f"UI_AUDIT_OK mode={'full' if args.all else 'scoped'} scopes={','.join(scopes)} images={sum(checked.values())}")


if __name__ == "__main__":
    main()
