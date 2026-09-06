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
SCOPES = (*ASSET_GROUPS, "minimap", "scenario", "integrity")
ARCHIVE_SHAPES = {"G_Main.s9": (21, 98), "G_Common.s9": (67, 491), "G_Marker.s9": (5, 34), "G_MMap.s9": (16, 221)}


def expected_roles(groups):
    roles = []
    for group in groups:
        for name, _, columns, _ in ASSET_GROUPS[group][3]:
            roles.extend(name if frame == 0 else f"{name}-{frame}" for frame in range(columns))
    assert len(roles) == len(set(roles)), "Duplicate roles in independent audit catalog"
    return set(roles)


def verify_role_coverage(skin, groups):
    expected = expected_roles(groups)
    missing = expected - skin["roles"].keys()
    assert not missing, f"Missing selected roles: {sorted(missing)}"
    if set(groups) == set(ASSET_GROUPS):
        unknown = skin["roles"].keys() - expected
        assert not unknown, f"Roles without independent source coverage: {sorted(unknown)}"


def verify_asset_hash(root, asset):
    assert hashlib.sha256((root / asset["path"]).read_bytes()).hexdigest().upper() == asset["sha256"], asset["path"]


def verify_skin_assets(root, data, skin, bindings, groups):
    verify_role_coverage(skin, groups)
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
            assert rgba[a + 3] == (expected_alpha if visible else 0)
            if visible:
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
        if u16(0x1c) > 3:
            continue
        expected_ids.append(index)
        entry = next(e for e in entries if e["state"]["id"] == index)
        state = entry["state"]
        assert state["settlementId"] == u16(0x18)
        assert state["acted"] == bool(struct.unpack_from("<I", raw)[0] & 0x1000)
        assert state["injury"] == u16(0x2a)
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


def verify_scenario(content, data):
    package, p, text = content["map"], content["scenario"], content["text"]["entries"]
    from parse_pk_text import decode_ls11, split_indexed_records, strip_common_terminator
    messages = split_indexed_records(decode_ls11((data / "M_MsgPK.s9").read_bytes())[0])[2]
    hints = {"information":8261, "functions":8260, "help":8262, "pause":8439,
             "territory":8430, "map-large":8291, "map-small":8293, "advance":8253,
             "facilities":8290, "officers":8289, "units":8288,
             "overview-forces":8440, "overview-facilities":8442, "overview-units":8441}
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
    assert p["units"] == [] and all(struct.unpack_from("<H",raw["unit"],i*40+18)[0] == 0 for i in range(800))
    assert all(s["kind"] == (0 if s["id"] < 50 else 1) for s in p["settlements"])
    assert sum(s["id"] < 50 for s in p["settlements"]) == 50
    assert all("cityMarkerColor" not in s and "hasAvailableOfficer" not in s for s in p["settlements"])
    assert len([a for a in package["assets"] if a["id"].startswith("building-")]) == 624
    assert len(p["settlements"]) == 94
    player = next(f for f in p["forces"] if f["id"] == p["playerForceId"])
    officers = [o["state"] for o in p["officers"]]
    assert len(officers) == 195
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
    assert content["schemaVersion"] == 7
    assert package["schemaVersion"] == 8 and "presentation" not in package
    assert p["schemaVersion"] == 6 and skin["schemaVersion"] == 7
    assert not {a["id"] for a in skin["assets"]}.intersection(a["id"] for a in package["assets"])
    groups = [scope for scope in scopes if scope in ASSET_GROUPS]
    checked = {}
    if groups:
        bindings = {}
        for start, count in [(0x24ea90, 1425), (0x2601d0, 1145)]:
            for i in range(count):
                resource, palette = struct.unpack_from("<2I", exe, start + 8 * i)
                bindings[resource & 65535] = palette
        checked = verify_skin_assets(args.package, args.data, skin, bindings, groups)
    if "minimap" in scopes:
        asset = next(a for a in package["assets"] if a["id"] == "minimap-terrain")
        verify_asset_hash(args.package, asset)
        with Image.open(args.package / asset["path"]) as mini:
            verify_minimap(mini, package["squares"], (args.data / "P_MapEtc.s9").read_bytes()[44:1068])
        assert (asset["width"], asset["height"]) == (320, 160)
        assert len(package["boundaries"]) > 0
    if "scenario" in scopes:
        verify_scenario(content, args.data)
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
