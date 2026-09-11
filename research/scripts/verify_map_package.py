"""Private integration audit for BEH-RENDER-0002 (RE-SAN9PK-0053/0055/0148); never used by the product."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from PIL import Image, ImageChops
from square_map.graphics import parse_image_stream, parse_palettes
from square_map.resources import MapVisualResources
from square_map.scenario import read_squares
from square_map.scenario import read_building_snapshot
from square_map.canvas import RgbCanvas
from square_map.terrain import global_square_center
from square_map.clouds import build_edge_polylines, _polyline_x_by_y, draw_map_edge_cloud_foreground
from square_map.render_buildings import collect_building_visual_bounds
from square_map.buildings import select_building_composition, building_sprite_resource_id
from square_map.building_compositions import BUILDING_COMPOSITIONS
from square_map.terrain import region_building_base
import square_map.render_map as reference

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('exe',type=Path);parser.add_argument('data',type=Path);parser.add_argument('package',type=Path);parser.add_argument('output',type=Path)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    content=json.loads((args.package/'content.json').read_text(encoding='utf-8'))
    assert content['schemaVersion']==23
    texture_root=(args.package/content['textures']['path']).resolve()
    textures=json.loads((texture_root/'textures.json').read_text(encoding='utf-8'))
    package=dict(textures['map'], **content['map'])
    def image_region(asset):
        with Image.open(texture_root/asset['file']['path']) as image:
            x,y=asset['x'],asset['y']
            return image.crop((x,y,x+asset['width'],y+asset['height']))
    terrain_assets={s['id']:{str(a['id']):a for a in s['textures']} for s in package['seasons']}
    raw=args.exe.read_bytes()
    assert hashlib.sha256(raw).hexdigest()=='d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028'
    pairs=[struct.unpack_from('<2I',raw,0x25cfd0+i*8) for i in range(0x42e)]
    registry={r:p-91 for r,p in pairs if 10006<=r<=10776};assert len(registry)==771
    images=parse_image_stream(args.data/'G_LLand.s9')
    season_pixels=[[] for _ in images]
    for season,suffix in zip(['spring','summer','autumn','winter'],'SNAW'):
        palettes=parse_palettes(args.data/f'P_Land_{suffix}.s9')
        for i,image in enumerate(images):
            palette=palettes[registry[10006+i]];rgb=bytearray(len(image.pixels)*3)
            for c in range(3):rgb[c::3]=image.pixels.translate(bytes(p[c] for p in palette))
            actual=image_region(terrain_assets[season][str(10006+i)]).convert('RGB')
            assert actual.size==(image.width,image.height) and actual.tobytes()==rgb
            season_pixels[i].append(bytes(rgb))
    assert all(len(set(h))==4 for h in season_pixels)
    print('771 textures x 4 seasons: source-decoded RGB identical; every texture has 4 distinct outputs.',flush=True)
    # Preserve full-map equal-key insertion history, then clip drawing to the crop.
    reference.select_bridge_resource=lambda square:None
    scenario=args.data/'D_Sce002.S9'
    resources=MapVisualResources.load(args.data,scenario,'S',include_clouds=True)
    squares=read_squares(scenario)
    left,right=build_edge_polylines(squares,128)
    for points,field in [(left,'leftEdge'),(right,'rightEdge')]:
        rows=_polyline_x_by_y(points)
        assert package[field]==[rows.get(y) for y in range(6656)]
    snapshot=read_building_snapshot(scenario)
    bounds=collect_building_visual_bounds(squares,snapshot.fixed_buildings,snapshot.buildings,resources.buildings,0,0,200,200,128)
    class CloudRecorder:
        width=13056; height=6656
        def __init__(self):self.items=[]
        def draw_indexed(self,image,palette,x,y,**kwargs):
            index=next(i for i,item in enumerate(resources.clouds.images) if item is image)
            self.items.append({'assetId':f'cloud-{10777+index}','x':x,'y':y})
    recorder=CloudRecorder()
    draw_map_edge_cloud_foreground(recorder,squares,resources.clouds,128,bounds)
    assert package['clouds']==recorder.items
    print('Cloud scanline boundaries and all 220 foreground placements match independent reference.',flush=True)
    sort=reference.sort_land_draw_calls
    def clipped_sort(calls):
        for call in sort(calls):
            x,y=global_square_center(call[2],call[3])
            if 6880-256<x+96<7680+256 and 2610-256<y+112<3060+256:
                yield call
    class CropCanvas:
        def __init__(self,width,height):
            self.width=width;self.height=height;self.crop=RgbCanvas(800,450)
        def put(self,x,y,color,weight=255):self.crop.put(x-6880,y-2610,color,weight)
        def put_mask_edge(self,x,y,color,weight):self.crop.put_mask_edge(x-6880,y-2610,color,weight)
        def to_bytes(self):return self.crop.to_bytes()
    reference.sort_land_draw_calls=clipped_sort
    reference.RgbCanvas=CropCanvas
    result=reference.render_region(squares,resources,0,0,200,200)
    image=Image.frombytes('RGB',(800,450),result[2])
    image.save(args.output/'reference-terrain.png')
    # Full-map pixel origin for the crop: (6080,2160)+(800,450).
    actual_path=args.output/'product-terrain.png'
    if actual_path.exists():
        actual=Image.open(actual_path).convert('RGB');diff=ImageChops.difference(image,actual)
        assert not diff.getbbox(),f'Terrain mismatch: {diff.getbbox()}'
        print('Terrain 800x450 RGB comparison: zero differing pixels.',flush=True)
    composite_path=args.output/'product-composite.png'
    if actual_path.exists() and composite_path.exists():
        # Compare only blend semantics, retaining RGB888 in both paths. The
        # 129-value original alpha domain is recoverable from the converted byte.
        old=bytearray(Image.open(actual_path).convert('RGB').tobytes())
        assets={a['id']:a for a in package['assets']}
        building_sprites=[]
        for b in sorted(snapshot.buildings,key=lambda b:(b.x+b.y,b.x)):
            composition=select_building_composition(snapshot.fixed_buildings[b.fixed_building_code],b.durability,b.city_terrain_code)
            assert composition is not None
            x,y=region_building_base(b.x,b.y,200,squares[b.y*200+b.x].offset)
            for part in BUILDING_COMPOSITIONS[composition]:
                resource=building_sprite_resource_id(b,part)
                building_sprites.append({'assetId':f'building-{resource}-'+('alpha' if part.dynamic_flag else 'key'),'x':128+x+part.offset_x,'y':128+y+part.offset_y})
        sprites=package['bridges']+building_sprites+package['clouds']
        for sprite in sprites:
            asset=assets[sprite['assetId']];x,y=sprite['x'],sprite['y']
            if x>=7680 or y>=3060 or x+asset['width']<=6880 or y+asset['height']<=2610:continue
            rgba=image_region(asset).convert('RGBA');pixels=rgba.tobytes()
            for py in range(max(2610,y),min(3060,y+rgba.height)):
                for px in range(max(6880,x),min(7680,x+rgba.width)):
                    src=((py-y)*rgba.width+px-x)*4;dst=((py-2610)*800+px-6880)*3
                    alpha=(pixels[src+3]*128+127)//255
                    if not alpha:continue
                    for c in range(3):old[dst+c]=pixels[src+c] if alpha>=128 else (pixels[src+c]*alpha+old[dst+c]*(127-alpha))//128
        actual=Image.open(composite_path).convert('RGB').tobytes()
        differences=[abs(a-b) for a,b in zip(actual,old)]
        print(json.dumps({'alphaMeanChannelDifference':sum(differences)/len(differences),'alphaMaxChannelDifference':max(differences),'alphaPixelsAboveOnePercent':sum(max(differences[i:i+3])>2.55 for i in range(0,len(differences),3))}),flush=True)
    size=sum(p.stat().st_size for p in args.package.rglob('*') if p.is_file())
    print(json.dumps({'packageBytes':size,'sourceRegistryEntries':len(registry),'terrainCrop':[6880,2610,800,450]}),flush=True)

if __name__=='__main__':main()
