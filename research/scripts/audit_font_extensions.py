"""Audit every original font slot and private icon route (RE-SAN9PK-0159).
Writes private source-derived evidence only to the explicitly supplied output directory.
"""
import argparse
import collections
import hashlib
import json
import struct
from pathlib import Path
from PIL import Image, ImageDraw
from parse_pk_text import decode_ls11, split_indexed_records, strip_common_terminator
from verify_initial_ui import parse_combined

BANKS = [(24, 0x14410), (16, 0x66C410), (12, 0x93E810)]


def decode(code, encoding):
    try:
        return code.to_bytes(2, "big").decode(encoding)
    except UnicodeDecodeError:
        return None


def font_slots(data):
    count = struct.unpack_from("<I", data)[0]
    if count != 23040 or len(data) != 11352080:
        raise ValueError("Unexpected F_Font sample layout")
    codes = struct.unpack_from(f"<{count}H", data, 4)
    if codes != tuple(range(0xA100, 0xFB00)):
        raise ValueError("Unexpected font code table")
    return codes


def glyph(data, slot, size, offset):
    start = offset + slot * size * size // 2
    raw = data[start:start + size * size // 2]
    if len(raw) != size * size // 2:
        raise ValueError("Truncated glyph")
    return Image.frombytes("L", (size, size), bytes(255 - value * 17 for byte in raw for value in (byte & 15, byte >> 4)))


def private_index(code):
    return code - 0xFAB0 if 0xFAB0 <= code <= 0xFAFC else None


def executable_offset(exe, address):
    pe = struct.unpack_from("<I", exe, 0x3c)[0]
    count = struct.unpack_from("<H", exe, pe+6)[0]
    size = struct.unpack_from("<H", exe, pe+20)[0]
    base = struct.unpack_from("<I", exe, pe+24+28)[0]
    rva = address - base
    for index in range(count):
        virtual_size, virtual, raw_size, raw = struct.unpack_from("<4I", exe, pe+24+size+40*index+8)
        if virtual <= rva < virtual + raw_size:
            return raw + rva - virtual
    raise ValueError("Address outside file sections")


def contact(data, codes, output):
    # Three native size banks, enlarged with nearest-neighbor; no font substitution.
    for page, start in enumerate(range(0, len(codes), 60)):
        subset = codes[start:start+60]
        image = Image.new("RGB", (1200, ((len(subset)+5)//6)*125), "white")
        draw = ImageDraw.Draw(image)
        for index, code in enumerate(subset):
            x, y = index%6*200, index//6*125
            draw.text((x+4,y+4), f"{code:04X}   24 / 16 / 12", fill="black")
            for bank, (size, offset) in enumerate(BANKS):
                image.paste(glyph(data, code-0xA100, size, offset).resize((60,60), Image.Resampling.NEAREST), (x+bank*65,y+25))
        image.save(output / f"slots-{page:02}.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("data", type=Path)
    parser.add_argument("exe", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    font = (args.data/"F_Font.s9").read_bytes()
    exe = args.exe.read_bytes()
    if hashlib.sha256(exe).hexdigest() != "d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028":
        raise ValueError("Expected registered TC-101 executable")
    codes = font_slots(font)
    rows = []
    for index, code in enumerate(codes):
        standard = decode(code, "cp950")
        if standard is not None:
            continue
        pixels = []
        for size, offset in BANKS:
            raw = font[offset+index*size*size//2:offset+(index+1)*size*size//2]
            pixels.append(sum((v&15)!=0 for v in raw)+sum((v>>4)!=0 for v in raw))
        rows.append(dict(code=f"{code:04X}", slot=index, valid_big5_trail=0x40<=code%256<=0x7e or 0xa1<=code%256<=0xfe,
            nonzero_pixels=pixels, hkscs_projection=decode(code,"big5hkscs"), private_icon_index=private_index(code)))
    table_offset = executable_offset(exe, 0x653318)
    routes=[]
    for code in range(0xFAB0,0xFAFD):
        values=[]
        for size_class in range(3):
            index=48*size_class+private_index(code)
            resource=struct.unpack_from("<i",exe,table_offset+4*index)[0]
            values.append(dict(size=BANKS[size_class][0], resource=resource, inside_declared_size_row=private_index(code)<48))
        routes.append(dict(code=f"{code:04X}", glyph_index=private_index(code), routes=values))
    # Plain records only: event bytecode is deliberately not treated as encoded text.
    usage=collections.defaultdict(list)
    skipped={}
    for path in sorted(args.data.glob("M_*.s9")):
        records=split_indexed_records(decode_ls11(path.read_bytes())[0])[2]
        skip=0
        for record_id, record in enumerate(records):
            raw,_=strip_common_terminator(record)
            if any(c<32 and c not in (9,10,13) for c in raw):
                skip+=1
                continue
            index=0
            pending=[]
            valid=True
            while index<len(raw):
                if 0xa1<=raw[index]<=0xfa and index+1<len(raw):
                    code=int.from_bytes(raw[index:index+2],"big")
                    if decode(code,"cp950") is None:
                        pending.append((f"{code:04X}",dict(file=path.name,record=record_id,byte_offset=index)))
                    index+=2
                else:
                    if raw[index]>=128:
                        valid=False
                        break
                    index+=1
            if valid:
                for code, occurrence in pending:
                    usage[code].append(occurrence)
        skipped[path.name]=skip
    visible=[int(row["code"],16) for row in rows if any(row["nonzero_pixels"])]
    contact(font,visible,args.output)
    common_palettes, common_images=parse_combined((args.data/"G_Common.s9").read_bytes())
    icon_image=Image.new("RGB",(960,((len(routes)+7)//8)*110),(32,32,32));draw=ImageDraw.Draw(icon_image)
    for j,row in enumerate(routes):
        x,y=j%8*120,j//8*110
        draw.text((x,y),row["code"],fill="white")
        for k,route in enumerate(row["routes"]):
            resource=route["resource"]
            draw.text((x+k*40,y+75),str(resource),fill="white")
            if not route["inside_declared_size_row"] or resource<0:
                continue
            width,height,pixels=common_images[resource]
            binding=0x24EA90+8*resource
            resource_code,palette=struct.unpack_from("<2I",exe,binding)
            if resource_code&65535 != resource:
                raise ValueError("Unexpected resource registry")
            pal=common_palettes[palette-200]
            rgb=bytes(component for pixel in pixels for component in pal[pixel*4:pixel*4+3])
            icon=Image.frombytes("RGB",(width,height),rgb)
            icon.thumbnail((36,50),Image.Resampling.NEAREST)
            icon_image.paste(icon,(x+k*40,y+22))
    icon_image.save(args.output/"icons.png")
    report=dict(finding="RE-SAN9PK-0159",font_sha256=hashlib.sha256(font).hexdigest(),exe_sha256=hashlib.sha256(exe).hexdigest(),
        total_slots=len(codes),cp950_undefined_slots=len(rows),nonempty_undefined_slots=len(visible),
        slots=rows,private_icon_routes=routes,plain_text_usage=dict(sorted(usage.items())),control_records_excluded=skipped)
    (args.output/"inventory.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in report.items() if k not in ("slots","private_icon_routes","plain_text_usage")},ensure_ascii=False))
    print("Plain-text extension codes:",sorted(usage))
    import html
    table="".join(f"<tr><td>{r['code']}</td><td>{r['slot']}</td><td>{r['valid_big5_trail']}</td><td>{r['nonzero_pixels']}</td><td>{html.escape(r['hkscs_projection'] or '')}</td></tr>" for r in rows)
    images="".join(f'<h3>Slots {i*60+1}–{min((i+1)*60,len(visible))}</h3><img src="slots-{i:02}.png">' for i in range((len(visible)+59)//60))
    document=f"""<!doctype html><meta charset="utf-8"><title>原版扩展码位核对</title>
<style>body{{font:16px sans-serif;max-width:1250px;margin:30px auto}}img{{max-width:100%}}td,th{{border:1px solid #ccc;padding:6px}}table{{border-collapse:collapse}}</style>
<h1>原版扩展码位核对 / RE-SAN9PK-0159</h1>
<p>完整遍历23040个字体槽；以下9288项是CP950未定义码位，其中917项至少一档含非零像素。非零不等于有效文字：保留槽含残留像素，不能全部登记为扩展字。</p>
<p>原字体三档24/16/12逐档提取，灰度仅用于查看覆盖度。HKSCS列仅供对比，绝不是原字形的Unicode判定。</p>
<p>原版私有图标语法接受FAB0–FAFC共77码；资源表每字号只有48项。FAE0起超出本字号行，不能把邻接数据误当有效图标。负数表示未绑定。</p>
<h2>私有图标路径</h2><img src="icons.png"><h2>全部非空的CP950未定义槽位</h2>{images}
<details><summary>全部9288项码位与三档像素清单</summary><table><tr><th>Code</th><th>Slot</th><th>合法Big5尾字节</th><th>非零像素24/16/12</th><th>HKSCS对比</th></tr>{table}</table></details>"""
    (args.output/"index.html").write_text(document,encoding="utf-8")

if __name__=="__main__":
    main()
