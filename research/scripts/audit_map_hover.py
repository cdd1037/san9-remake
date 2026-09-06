"""Independently compare hover assets and commander flags with local source data."""
from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

from PIL import Image
from square_map.scenario import parse_scenario_sections


def audit(data_dir: Path, package_root: Path) -> dict:
    package = json.loads((package_root / "content.json").read_text(encoding="utf-8"))
    raw = (data_dir / "G_Common.s9").read_bytes()
    palettes = [raw[4 + i * 1064 + 40:4 + (i + 1) * 1064] for i in range(67)]
    images = []
    offset = 4 + 67 * 1064 + 4
    while offset < len(raw):
        size, stride, width, height = struct.unpack_from("<4I", raw, offset)
        assert stride == width and size == 16 + width * height
        images.append((width, height, raw[offset + 16:offset + size]))
        offset += size
    assert len(images) == 491
    assets = {a["id"]: a for a in package["skin"]["assets"]}
    verified = []
    for theme, base, palette_id in [("own", 398, 53), ("other", 407, 54), ("friendly", 416, 55)]:
        parts = images[base:base + 9]
        palette = palettes[palette_id]
        for kind, height in [("city", 316), ("facility", 248)]:
            role = f"hover-{theme}-{kind}"
            path = assets[package["skin"]["roles"][role]]["path"]
            actual = Image.open(package_root / path).convert("RGBA")
            assert actual.size == (160, height)
            for y in range(height):
                for x in range(160):
                    column = 0 if x < parts[3][0] else 2 if x >= 160 - parts[5][0] else 1
                    row = 0 if y < parts[1][1] else 2 if y >= height - parts[7][1] else 1
                    part = row * 3 + column
                    w, h, indices = parts[part]
                    sx = x - (160 - w) if column == 2 else x
                    sy = y - (height - h) if row == 2 else y
                    index = indices[(sy % h) * w + sx % w]
                    color = palette[index * 4:index * 4 + 4]
                    alpha = 0 if color == palette[:4] else (min(color[3], 128) * 255 + 64) // 128
                    pixel = actual.getpixel((x, y))
                    assert pixel[3] == alpha, (role, x, y, "alpha")
                    if alpha:
                        assert pixel[:3] == tuple(color[:3]), (role, x, y, "color")
            verified.append(role)
    persons = parse_scenario_sections((data_dir / "D_Sce002.S9").read_bytes())["prsn"]
    source_ids = {i for i in range(700) if struct.unpack_from("<I", persons, i * 134)[0] & 4}
    imported_ids = {o["state"]["id"] for o in package["scenario"]["officers"] if o["state"]["isCommander"]}
    assert source_ids == imported_ids
    active = [o["state"] for o in package["scenario"]["officers"] if o["state"]["isCommander"] and o["state"]["status"] <= 3]
    assert len({o["settlementId"] for o in active}) == len(active)
    assert {s["id"] for s in package["scenario"]["settlements"]} == {b["id"] for b in package["map"]["buildings"]}
    return {"verified_backgrounds": verified, "commander_flags": len(source_ids), "serving_commanders": len(active),
            "settlements": len(package["scenario"]["settlements"]), "officers": len(package["scenario"]["officers"])}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-dir", type=Path, required=True)
    parser.add_argument("--package", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(audit(args.data_dir, args.package), ensure_ascii=False))
