"""Prepare fixed PBR fields from CC0 scans and completed piece-word images.
No font, generic glyph atlas, reference image or camera-dependent mapping is exported.
Normal maps use Unity packed tangents (X in alpha, Y in green).
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'assets/source/polyhaven'
OUT = ROOT / 'Unity/Assets/Showcase/Resources/PieceTextures'
PBR = ROOT / 'Unity/Assets/Resources/ShowcasePbr'
OUT.mkdir(parents=True, exist_ok=True)
PBR.mkdir(parents=True, exist_ok=True)
N = 2048
VERSION = 'pbr-courtyard/3.0'


def read(asset, kind, rotate=False):
    a = np.asarray(Image.open(SOURCE / f'{asset}_{kind}.jpg').convert('RGB'), dtype=np.float32) / 255
    return np.rot90(a).copy() if rotate else a


def save(path, data):
    Image.fromarray(np.uint8(np.clip(data * 255 + .5, 0, 255))).save(path)


def pack_normal(rgb, scale=1):
    xy = (rgb[..., :2] * 2 - 1) * scale
    z = np.sqrt(np.maximum(1 - np.sum(xy * xy, axis=2), .01))
    xyz = np.dstack((xy, z))
    xyz /= np.maximum(np.linalg.norm(xyz, axis=2, keepdims=True), 1e-7)
    return np.dstack((np.ones_like(z), xyz[..., 1] * .5 + .5, xyz[..., 2] * .5 + .5, xyz[..., 0] * .5 + .5))


def mask(smooth, ao=1):
    return np.dstack((np.zeros_like(smooth), np.broadcast_to(ao, smooth.shape), np.ones_like(smooth), smooth))


# Retain photographed growth rings, pores and polish variation.
walnut = read('wood_table_001', 'diff')
walnut[..., 0] *= .94; walnut[..., 1] *= 1.02; walnut[..., 2] *= 1.10
rough = read('wood_table_001', 'Rough')[..., 0]
save(PBR / 'Walnut_Color.png', walnut)
save(PBR / 'Walnut_Normal.png', pack_normal(read('wood_table_001', 'nor_gl'), .32))
save(PBR / 'Walnut_Mask.png', mask(np.clip(.70 - rough * .65, .34, .70), .9 + read('wood_table_001', 'AO')[..., 0] * .1))

# Neutral walnut scan recoloured to restrained honey. This is an authored wood
# appearance, not a claim that the scan depicts kaya timber.
scan = read('american_walnut_veneer', 'diff', rotate=True)
luma = np.mean(scan, axis=2)
deviation = np.clip((luma - np.median(luma)) * 1.6, -.22, .18)
wood = np.dstack((.79 + deviation, .61 + deviation * .90, .37 + deviation * .62))
rough = np.rot90(read('american_walnut_veneer', 'Rough')[..., 0]).copy()
smooth = np.clip(.60 - rough * .18, .39, .58)
raw_normal = read('american_walnut_veneer', 'nor_gl', rotate=True)
raw_normal = np.dstack((raw_normal[..., 1], 1 - raw_normal[..., 0], raw_normal[..., 2]))
wood_normal = pack_normal(raw_normal, .16)
save(PBR / 'Honey_Color.png', wood)
save(PBR / 'Honey_Normal.png', wood_normal)
save(PBR / 'Honey_Mask.png', mask(smooth))
save(OUT / 'Wood.png', wood)

# A seamless natural sandstone surface: no masonry grid or mortar is baked in.
# Preserve real mineral grains and shallow fissures while adapting warm cream tone.
stone = read('sandstone_cracks', 'diff')
stone_luma = stone[..., 0] * .2126 + stone[..., 1] * .7152 + stone[..., 2] * .0722
stone_detail = (stone_luma - np.median(stone_luma)) * .90
stone_color = np.dstack((.70 + stone_detail, .67 + stone_detail * .97, .60 + stone_detail * .92))
stone_color += (stone - stone_luma[..., None]) * .12
save(PBR / 'Sandstone_Color.png', np.clip(stone_color, .30, .88))
save(PBR / 'Sandstone_Normal.png', pack_normal(read('sandstone_cracks', 'nor_gl'), .22))
stone_rough = read('sandstone_cracks', 'Rough')[..., 0]
stone_ao = .85 + read('sandstone_cracks', 'AO')[..., 0] * .15
save(PBR / 'Sandstone_Mask.png', mask(np.clip(.40 - stone_rough * .21, .18, .36), stone_ao))

font_path = Path('C:/Windows/Fonts/yumindb.ttf')
if not font_path.exists():
    raise FileNotFoundError('Yu Mincho Demibold required; no undocumented substitution')
font = ImageFont.truetype(str(font_path), 640)
labels = {'Pawn': '歩兵', 'Lance': '香車', 'Knight': '桂馬', 'Silver': '銀将',
          'Gold': '金将', 'Bishop': '角行', 'Rook': '飛車', 'KingA': '玉将', 'KingB': '王将'}


def surface(name, ink_image, wood_field, base_smooth, normal_field, engraving_depth):
    ink = np.asarray(ink_image, dtype=np.float32) / 255
    color = wood_field * (1 - ink[..., None]) + np.array([.028, .020, .014]) * ink[..., None]
    save(OUT / f'{name}.png', color)
    relief = np.asarray(ink_image.filter(ImageFilter.GaussianBlur(3)), dtype=np.float32) / 255
    # Shallow filled carving changes shading only: 0.04 mm for words, 0.025 mm grid.
    pitch = .45 / N if name == 'Board' else .029 / N
    gy, gx = np.gradient(-relief * engraving_depth, pitch)
    nx = (normal_field[..., 3] * 2 - 1) - gx
    ny = (normal_field[..., 1] * 2 - 1) + gy
    nz = np.ones_like(nx)
    xyz = np.dstack((nx, ny, nz)); xyz /= np.linalg.norm(xyz, axis=2, keepdims=True)
    packed = np.dstack((nz, xyz[..., 1] * .5 + .5, xyz[..., 2] * .5 + .5, xyz[..., 0] * .5 + .5))
    save(OUT / f'{name}_Normal.png', packed)
    save(OUT / f'{name}_Mask.png', mask(base_smooth * (1 - ink) + .25 * ink, 1 - relief * .16))


board_mask = Image.new('L', (N, N)); draw = ImageDraw.Draw(board_mask)
for i in range(10):
    q = round(i * (N - 1) / 9)
    draw.line((q, 0, q, N-1), fill=215, width=2)
    draw.line((0, q, N-1, q), fill=215, width=2)
for ix in (3, 6):
    for iy in (3, 6):
        xx = ix * (N-1) / 9; yy = iy * (N-1) / 9
        draw.ellipse((xx-3, yy-3, xx+3, yy+3), fill=225)
surface('Board', board_mask, wood, smooth, wood_normal, .000025)
for index, (name, label) in enumerate(labels.items()):
    ink_image = Image.new('L', (N, N)); draw = ImageDraw.Draw(ink_image)
    for i, char in enumerate(label):
        box = draw.textbbox((0, 0), char, font=font, stroke_width=7)
        xx = (N - (box[2] - box[0])) / 2 - box[0]
        yy = 300 + i * 660 - box[1]
        draw.text((xx, yy), char, font=font, fill=255, stroke_width=7)
    ink_image = ink_image.filter(ImageFilter.GaussianBlur(.45))
    shift = index * 113
    surface(name, ink_image, np.roll(wood, shift, axis=1), np.roll(smooth, shift, axis=1), np.roll(wood_normal, shift, axis=1), .00004)

records = []
for asset in ('wood_table_001', 'american_walnut_veneer', 'sandstone_cracks'):
    info = json.loads((SOURCE / f'{asset}-info.json').read_text(encoding='utf-8-sig'))
    files = json.loads((SOURCE / f'{asset}-files.json').read_text(encoding='utf-8-sig'))
    for kind, key in [('diff','Diffuse'), ('nor_gl','nor_gl'), ('Rough','Rough'), ('AO','AO')]:
        p = SOURCE / f'{asset}_{kind}.jpg'
        k = next(k for k in files if k.lower() == key.lower())
        record = files[k]['2k']['jpg']
        assert hashlib.md5(p.read_bytes()).hexdigest() == record['md5'], p
        records.append({'asset': asset, 'authors': info['authors'], 'kind': kind,
                        'sourcePage': f'https://polyhaven.com/a/{asset}', 'downloadUrl': record['url'],
                        'license': 'CC0-1.0', 'licenseUrl': 'https://polyhaven.com/license',
                        'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'path': str(p.relative_to(ROOT))})
report = {'schema': 'showcase-pbr-provenance/3', 'materialVersion': VERSION, 'preparedDate': '2026-10-15',
          'sourceAssets': records, 'font': 'Installed Yu Mincho Demibold; finished words only; font not distributed',
          'changes': ['CC0 scans recoloured and normal strength adjusted', 'roughness converted to HDRP smoothness mask',
                      'original fixed word ink/normal/AO detail', 'sandstone colour adapted to warm cream, normal strength 0.22 and AO >= 0.85; no mortar grid',
                      'no displacement, parallax or image projection'],
          'outputs': [{'path': str(p.relative_to(ROOT)), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                      for folder in (OUT, PBR) for p in sorted(folder.glob('*.png'))]}
(SOURCE / 'provenance.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(f'{VERSION}: {len(labels)} completed word surfaces + board + 3 CC0 scan surfaces; source MD5 verified')
