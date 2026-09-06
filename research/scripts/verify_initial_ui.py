"""Independent private-sample audit for BEH-UI-0001 / RE-SAN9PK-0157."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from PIL import Image


UI_RECORDS = {
    "roster-scroll-decoration": (85, 1),
    "button-scroll-up": (51, 4), "button-scroll-down": (52, 4),
    "scroll-track-top": (53, 1), "scroll-track-middle": (54, 1), "scroll-track-bottom": (55, 1),
    "scroll-thumb-top": (56, 1), "scroll-thumb-middle": (57, 1), "scroll-thumb-bottom": (58, 1), "field-left": (91, 1), "field-middle": (92, 1), "field-right": (93, 1),
    "ruler-frame": (94, 1), "region-frame": (97, 1),
    "top-frame": (88, 1), "radar-frame": (82, 1), "sidebar-frame": (84, 1),
    "season-spring": (64, 1), "season-summer": (65, 1),
    "season-autumn": (66, 1), "season-winter": (67, 1),
    "button-territory": (68, 6), "button-facilities": (69, 4),
    "button-officers": (70, 4), "button-units": (71, 4),
    "button-information": (74, 4), "button-functions": (73, 4),
    "button-help": (75, 4), "button-zoom": (86, 4), "button-advance": (87, 4),
}


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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("data", type=Path)
    parser.add_argument("package", type=Path)
    parser.add_argument("--baseline", type=Path)
    args = parser.parse_args()
    exe = args.exe.read_bytes()
    assert hashlib.sha256(exe).hexdigest() == "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028"
    content = json.loads((args.package / "content.json").read_text(encoding="utf-8"))
    assert content["schemaVersion"] == 7
    map_root = skin_root = args.package
    package, p, skin = content["map"], content["scenario"], content["skin"]
    text = content["text"]["entries"]
    assert package["schemaVersion"] == 8 and "presentation" not in package
    assert p["schemaVersion"] == 5 and skin["schemaVersion"] == 6
    assets = {a["id"]: a for a in skin["assets"]}
    map_assets = {a["id"]: a for a in package["assets"]}
    assert not set(assets).intersection(map_assets)
    for root, entries in [(map_root, package["assets"] + [a for s in package["seasons"] for a in s["textures"]]), (skin_root, skin["assets"])]:
        for a in entries:
            assert hashlib.sha256((root / a["path"]).read_bytes()).hexdigest().upper() == a["sha256"]
    bindings = {}
    for start, count in [(0x24ea90, 1425), (0x2601d0, 1145)]:
        for i in range(count):
            resource, palette = struct.unpack_from("<2I", exe, start + 8 * i)
            bindings[resource & 65535] = palette
    palettes, images = parse_combined((args.data / "G_Main.s9").read_bytes())
    assert len(palettes) == 21 and len(images) == 98
    checked = 0
    for name, (index, columns) in UI_RECORDS.items():
        for frame in range(columns):
            key = name if frame == 0 else f"{name}-{frame}"
            asset = assets[skin["roles"][key]]
            with Image.open(skin_root / asset["path"]) as actual:
                verify_frame(actual, images[index], palettes[bindings[5975 + index] - 60], columns, frame, name == "region-frame")
            checked += 1
    extras = {"city-prefix":43,"message-left":165,"message-middle":166,"message-right":167,
        "bar-morale-frame":390,"bar-durability-frame":391,"bar-morale-normal":392,"bar-durability-normal":393,
        "bar-morale-critical":394,"bar-morale-warning":395,"bar-durability-critical":396,"bar-durability-warning":397,
        "icon-emperor":1243,"icon-training":1244,"icon-merchant":1245}
    common = parse_combined((args.data / "G_Common.s9").read_bytes())
    marker = parse_combined((args.data / "G_Marker.s9").read_bytes())
    for role, resource in extras.items():
        archive, base, palette_base = (common, 0, 200) if resource < 1220 else (marker, 1220, 311)
        with Image.open(skin_root / assets[skin["roles"][role]]["path"]) as actual:
            verify_frame(actual, archive[1][resource-base], archive[0][bindings[resource]-palette_base], 1, 0, role.startswith("message-"))
        checked += 1
    for part, resource in [("left",290),("middle",291),("right",292)]:
        assert bindings[resource] == 234
        for frame in range(4):
            role = "roster-row-" + part + (f"-{frame}" if frame else "")
            with Image.open(skin_root / assets[skin["roles"][role]]["path"]) as actual:
                verify_frame(actual, common[1][resource], common[0][34], 4, frame, True)
                if frame in (0,3):
                    assert not any(actual.getchannel("A").tobytes())
            checked += 1
    assert checked == len(skin["roles"]) == 92
    assert skin["colors"]["roster-available"] == "F0F0F0" and skin["colors"]["roster-unavailable"] == "988058"
    with Image.open(map_root / map_assets["minimap-terrain"]["path"]) as mini:
        verify_minimap(mini, package["squares"], (args.data/"P_MapEtc.s9").read_bytes()[44:1068])
    assert len(package["boundaries"]) > 0
    assert (map_assets["minimap-terrain"]["width"],map_assets["minimap-terrain"]["height"]) == (320,160)
    assert sum(s["id"] < 50 for s in p["settlements"]) == 50
    assert all("cityMarkerColor" not in s and "hasAvailableOfficer" not in s for s in p["settlements"])
    assert len([a for a in package["assets"] if a["id"].startswith("building-")]) == 624
    assert len(p["settlements"]) == 94
    player = next(f for f in p["forces"] if f["id"] == p["playerForceId"])
    officers = [o["state"] for o in p["officers"]]
    assert len(officers) == 195
    verify_officers((args.data / "D_Sce002.S9").read_bytes(), p["officers"], text, (args.data / "M_MsgPK.s9").read_bytes())
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
    if args.baseline:
        old = json.loads((args.baseline / "map.json").read_text(encoding="utf-8"))
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
    print(f"UI_AUDIT_OK: {checked} source-identical visible RGB/alpha assets; names, initial values, hashes and optional baseline verified.")


if __name__ == "__main__":
    main()
