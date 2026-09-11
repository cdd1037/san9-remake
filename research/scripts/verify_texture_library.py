"""从原版源文件独立核对最终纹理颜色、图集区域及原有显示图片。

python research/scripts/verify_texture_library.py --package local/generated/content/textures-san9pk-tc101 --source private/s9数据文件
"""
import argparse
import hashlib
import io
import json
import struct
from functools import lru_cache
from pathlib import Path

from PIL import Image


def source_images(data):
    header = struct.unpack_from("<I", data)[0]
    offset = 0 if header == len(data) else 4 + header * 1064
    if offset + 4 <= len(data) and struct.unpack_from("<I", data, offset)[0] == len(data) - offset:
        positions = []
        position = offset + 4
        while position < len(data):
            positions.append(position)
            position += struct.unpack_from("<I", data, position)[0]
        assert position == len(data)
    else:
        positions = struct.unpack_from(f"<{header}I", data, 4)
    result = []
    for position in positions:
        size, stride, width, height = struct.unpack_from("<4I", data, position)
        assert stride == width and size == 16 + width * height
        result.append(Image.frombytes("L", (width, height), data[position + 16:position + size]))
    return result


def verify(package, source, baseline=None, scenario=None):
    manifest = json.loads((package / "textures.json").read_text(encoding="utf-8"))

    @lru_cache(maxsize=8)
    def page(path):
        with Image.open(package / path) as image:
            return image.copy()

    def region(asset):
        x, y, w, h = (asset[k] for k in ("x", "y", "width", "height"))
        return page(asset["file"]["path"]).crop((x, y, x + w, y + h))

    originals = {p.stem.lower(): p for p in source.glob("G_*.s9")}
    excluded = {"g_lnmask", "g_lnuki", "g_title", "g_facel", "g_faces", "g_facet", "g_faclpk", "g_facspk", "g_factpk"}
    archives = manifest["library"]["archives"]
    assert set(originals) - excluded == {a["id"] for a in archives}, "Graphics archive coverage differs"
    external = {"g_facel": "p_face", "g_faces": "p_face", "g_facet": "p_face",
        "g_faclpk": "p_face", "g_facspk": "p_face", "g_factpk": "p_face", "g_lbuild": "p_lbuild",
        "g_lbadd": "p_badd", "g_lland": "p_land_s", "g_lhashi": "p_lndadd", "g_lmask": "p_lndadd",
        "g_lcloud": "p_lcloud", "g_mapmkr": "p_mapmkr", "g_mask": "p_mask", "g_mesh": "p_mesh",
        "g_mapcov": "p_mapetc", "g_dyneff": "p_dyneff", "g_ikki": "p_ikki", "g_gaikou": "p_gaikou", "g_zoomin": "p_zoomin"}
    palette_files = {p.stem.lower(): p for p in source.glob("P_*.s9")}
    counts = {"archives": len(archives), "rgb_variants": 0, "runtime_images": 0}
    for archive in archives:
        assert not {"indices", "palettes", "unresolved"}.intersection(archive), archive["id"]
        data = originals[archive["id"]].read_bytes()
        if data[:2] == b"BM":
            with Image.open(io.BytesIO(data)) as bitmap:
                assert bitmap.convert("RGBA").tobytes() == region(archive["variants"][0]["image"]).convert("RGBA").tobytes()
            counts["rgb_variants"] += 1
            continue
        images = source_images(data)
        palette_data = palette_files[external[archive["id"]]].read_bytes() if archive["id"] in external else data
        palette_count = struct.unpack_from("<I", palette_data)[0]
        for variant in archive["variants"]:
            assert 0 <= variant["paletteIndex"] < palette_count
            start = 44 + variant["paletteIndex"] * 1064
            palette = palette_data[start:start + 1024]
            indexed = images[variant["imageIndex"]].copy()
            indexed.putpalette(b"".join(palette[i:i + 3] for i in range(0, 1024, 4)))
            restored = region(variant["image"]).convert("RGBA")
            expected = indexed.convert("RGBA")
            opaque = archive["category"].startswith("portraits/") or archive["category"] == "illustrations" or archive["id"] == "g_lland"
            if not opaque:
                alpha = bytes(0 if palette[i * 4:i * 4 + 4] == palette[:4] else 255 for i in images[variant["imageIndex"]].tobytes())
                expected.putalpha(Image.frombytes("L", expected.size, alpha))
            assert restored.size == expected.size and restored.getchannel("A").tobytes() == expected.getchannel("A").tobytes(), variant["image"]["id"]
            # Alpha-border dilation may change only invisible RGB. Compare visible colors independently.
            blank = Image.new("RGBA", expected.size)
            mask = expected.getchannel("A")
            assert Image.composite(restored, blank, mask).tobytes() == Image.composite(expected, blank, mask).tobytes(), variant["image"]["id"]
            counts["rgb_variants"] += 1
        expected_images = set(range(len(images)))
        if archive["id"] == "g_still": expected_images -= set(range(72, 172))
        assert expected_images == {v["imageIndex"] for v in archive["variants"]}
    portraits = manifest.get("portraits", [])
    counts["portraits"] = len(portraits)
    if portraits:
        assert {p["id"] for p in portraits} == set(range(836))
        palette_data = palette_files["p_face"].read_bytes()
        palette = palette_data[44:44 + 1024]
        for archive, anchor_file, first in [("g_facel", "D_FacPnt.s9", 0), ("g_faclpk", "D_FcPtPK.s9", 786)]:
            images = source_images(originals[archive].read_bytes())
            offsets = (source / anchor_file).read_bytes()
            for i, indexed in enumerate(images):
                portrait = next(p for p in portraits if p["id"] == first + i)
                assert abs(portrait["anchorX"] - offsets[i * 2] / 240) < 1e-12
                assert abs(portrait["anchorY"] - offsets[i * 2 + 1] / 240) < 1e-12
                indexed.putpalette(b"".join(palette[n:n + 3] for n in range(0, 1024, 4)))
                assert region(portrait["image"]).convert("RGB").tobytes() == indexed.convert("RGB").tobytes()
    if baseline:
        old = json.loads(baseline.read_text(encoding="utf-8"))
        groups = {"map": manifest["map"]["assets"], "skin": manifest["skin"]["assets"]}
        groups.update({s["id"]: s["textures"] for s in manifest["map"]["seasons"]})
        for key, assets in groups.items():
            for asset in assets:
                image = region(asset).convert({1: "L", 3: "RGB", 4: "RGBA"}[old[key][asset["id"]][2]])
                actual = [image.width, image.height, len(image.getbands()), hashlib.sha256(image.tobytes()).hexdigest()]
                assert old[key][asset["id"]] == actual, asset["id"]
                counts["runtime_images"] += 1
        if scenario:
            instance = json.loads((scenario / "content.json").read_text(encoding="utf-8"))
            assert instance["schemaVersion"] == 22
            assert "assets" not in instance["map"] and "skin" not in instance
            catalog = {v["image"]["id"]: v["image"] for a in archives for v in a["variants"]}
            catalog.update({p["image"]["id"]: p["image"] for p in portraits})
            for role, asset_id in instance["illustrationRoles"].items():
                if role.startswith("catalog-person-"): continue  # Unified crop intentionally replaces original S pixels.
                old_id = old["roles"][role]
                asset = catalog[asset_id]
                image = region(asset).convert("RGBA")
                assert old["skin"][old_id] == [image.width, image.height, 4, hashlib.sha256(image.tobytes()).hexdigest()], role
                counts["runtime_images"] += 1
            merged_roles = dict(manifest["skin"]["roles"], **instance["illustrationRoles"])
            assert set(old["roles"]) == set(merged_roles)
            world = json.dumps(instance["scenario"], sort_keys=True, separators=(",", ":"), ensure_ascii=False)
            assert old["world"] == hashlib.sha256(world.encode("utf-8")).hexdigest(), "Scenario state changed"
    return counts


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--scenario", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    report = verify(args.package, args.source, args.baseline, args.scenario)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v if k != "unresolved" else {a: len(i) for a, i in v.items()} for k, v in report.items()}, ensure_ascii=False))
