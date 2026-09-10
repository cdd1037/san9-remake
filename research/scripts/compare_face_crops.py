"""Compare anchored L crops with original S/T, without modifying source assets."""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from export_g_face_palette0 import parse_image_stream, parse_palette


def metrics(a, b):
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    ga, gb = a.mean(axis=2).ravel(), b.mean(axis=2).ravel()
    return {
        "mae": float(np.abs(a - b).mean()),
        "rmse": float(np.sqrt(((a - b) ** 2).mean())),
        "gray_correlation": float(np.corrcoef(ga, gb)[0, 1]),
        "exact_pixel_fraction": float(np.all(a == b, axis=2).mean()),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    palette = np.frombuffer(parse_palette(args.source / "P_Face.s9"), dtype=np.uint8).reshape(256, 4)[:, :3]
    rows, portraits, originals = [], [], []
    filters = {name: getattr(Image.Resampling, name) for name in ("NEAREST", "BOX", "BILINEAR", "BICUBIC", "LANCZOS")}
    for expansion in (False, True):
        names = [f"G_Fac{s}PK.s9" if expansion else f"G_Face{s}.s9" for s in "LST"]
        archives = [parse_image_stream(args.source / name) for name in names]
        anchors = (args.source / ("D_FcPtPK.s9" if expansion else "D_FacPnt.s9")).read_bytes()
        expected_count = 50 if expansion else 786
        if any(len(archive) != expected_count for archive in archives):
            raise ValueError(f"Expected {expected_count} portraits in each of {names}")
        if len(anchors) != expected_count * 2:
            raise ValueError(f"Expected {expected_count} two-byte crop anchors")
        for index, records in enumerate(zip(*archives)):
            if [(w, h) for w, h, _ in records] != [(240, 240), (64, 80), (32, 40)]:
                raise ValueError(f"Unexpected portrait dimensions in {names} at record {index}")
            imgs = [Image.fromarray(palette[np.frombuffer(pixels, dtype=np.uint8).reshape(h, w)]) for w, h, pixels in records]
            large, small, tiny = imgs
            ax, ay = anchors[index * 2:index * 2 + 2]
            box = (ax + 32, ay + 30, ax + 128, ay + 150)
            if not (0 <= box[0] < box[2] <= large.width and 0 <= box[1] < box[3] <= large.height):
                raise ValueError(f"Crop {box} exceeds portrait bounds in {names[0]} at record {index}")
            crop = large.crop(box)
            pid = index + (786 if expansion else 0)
            portraits.append((pid, crop, small, tiny))
            originals.append((large, box))
            for relation, source, target in (("crop_to_S", crop, small), ("crop_to_T", crop, tiny), ("S_to_T", small, tiny)):
                for name, kernel in filters.items():
                    rows.append({"id": pid, "relation": relation, "filter": name, **metrics(source.resize(target.size, kernel), target)})
    summary = {}
    for relation in ("crop_to_S", "crop_to_T", "S_to_T"):
        summary[relation] = {}
        for name in filters:
            subset = [r for r in rows if r["relation"] == relation and r["filter"] == name]
            summary[relation][name] = {key: {"mean": float(np.mean([r[key] for r in subset])), "min": min(r[key] for r in subset), "max": max(r[key] for r in subset)} for key in ("mae", "rmse", "gray_correlation", "exact_pixel_fraction")}
    chosen = [r for r in rows if r["relation"] == "crop_to_S" and r["filter"] == "LANCZOS"]
    ranked = sorted(chosen, key=lambda r: r["mae"])
    alignment = []
    for result in chosen:
        if result["gray_correlation"] >= 0.99:
            continue
        pid = result["id"]
        large, original_box = originals[pid]
        target = portraits[pid][2]
        target_array = np.asarray(target, dtype=float)

        def error(x, y):
            resized = large.crop((x, y, x + 96, y + 120)).resize((64, 80), Image.Resampling.LANCZOS)
            return float(np.abs(np.asarray(resized, dtype=float) - target_array).mean())

        _, x, y = min((error(x, y), x, y) for x in range(0, 145, 4) for y in range(0, 121, 4))
        _, x, y = min((error(xx, yy), xx, yy) for xx in range(max(0, x - 4), min(144, x + 4) + 1) for yy in range(max(0, y - 4), min(120, y + 4) + 1))
        corrected_crop = large.crop((x, y, x + 96, y + 120))
        aligned = corrected_crop.resize((64, 80), Image.Resampling.LANCZOS)
        alignment.append({"id": pid, "original_xy": list(original_box[:2]), "searched_xy": [x, y], **metrics(aligned, target), "tiny": metrics(corrected_crop.resize((32, 40), Image.Resampling.LANCZOS), portraits[pid][3])})
    corrected_sheet = Image.new("RGB", (440, 175 * len(alignment) + 30), "#eeeeee")
    draw_corrected = ImageDraw.Draw(corrected_sheet)
    draw_corrected.text((8, 8), "ID | original S | adjusted crop -> S | original T | adjusted -> T", fill="black")
    for row, match in enumerate(alignment):
        pid = match["id"]
        x, y = match["searched_xy"]
        corrected_crop = originals[pid][0].crop((x, y, x + 96, y + 120))
        yy = 35 + 175 * row
        draw_corrected.text((4, yy), str(pid), fill="black")
        for xx, img, size in ((40, portraits[pid][2], (128, 160)), (180, corrected_crop.resize((64, 80), Image.Resampling.LANCZOS), (128, 160)), (320, portraits[pid][3], (32, 40)), (370, corrected_crop.resize((32, 40), Image.Resampling.LANCZOS), (32, 40))):
            corrected_sheet.paste(img.resize(size, Image.Resampling.NEAREST), (xx, yy))
    corrected_sheet.save(args.output / "adjusted-comparison.png")
    ids = list(dict.fromkeys([9, ranked[0]["id"], ranked[len(ranked)//2]["id"], *[r["id"] for r in ranked[-5:]], 786, 835]))
    sheet = Image.new("RGB", (780, 175 * len(ids) + 30), "#eeeeee")
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 8), "ID / L crop 96x120 | original S | crop -> S | original T | crop -> T   (S/T shown 2x nearest)", fill="black")
    for row, pid in enumerate(ids):
        _, crop, small, tiny = portraits[pid]
        y = 35 + row * 175
        draw.text((8, y), f"{pid}", fill="black")
        for x, img in ((55, crop), (165, small.resize((128, 160), Image.Resampling.NEAREST)), (310, crop.resize((64, 80), Image.Resampling.LANCZOS).resize((128, 160), Image.Resampling.NEAREST)), (460, tiny.resize((64, 80), Image.Resampling.NEAREST)), (550, crop.resize((32, 40), Image.Resampling.LANCZOS).resize((64, 80), Image.Resampling.NEAREST))):
            sheet.paste(img, (x, y))
        draw.text((635, y + 20), f"MAE {chosen[pid]['mae']:.2f}\ncorr {chosen[pid]['gray_correlation']:.4f}", fill="black")
    sheet.save(args.output / "comparison.png")
    (args.output / "metrics.json").write_text(json.dumps({"count": len(portraits), "summary": summary, "rows": rows, "alignment": alignment}, indent=2), encoding="utf-8")
    print(json.dumps({"count": len(portraits), "alignment": alignment}, indent=2))


if __name__ == "__main__":
    main()
