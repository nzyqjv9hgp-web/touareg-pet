# Rasterises Assets/model/touareg.svg into the layer PNGs the app draws (2x the 512 px frame).
# Needs: pip install cairosvg pillow. Run from the repository root: python3 tools/model/render.py
import io, json, os, re
import cairosvg
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), '..', '..')
SVG = os.path.join(ROOT, 'Assets', 'model', 'touareg.svg')
OUT = os.path.join(ROOT, 'Assets', 'model')
SCALE = 2
src = open(SVG, encoding='utf-8').read()
defs = re.search(r'<defs>.*?</defs>', src, re.S).group(0)


def group(name):
    tag = '<g id="%s">' % name
    start = src.index(tag) + len(tag)
    depth, i = 1, start
    while depth:
        a, b = src.find('<g', i), src.find('</g>', i)
        if a != -1 and a < b:
            depth += 1; i = a + 2
        else:
            depth -= 1; i = b + 4
    return src[start:i - 4]


def raster(inner, x, y, w, h):
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="%g %g %g %g" width="%d" height="%d">%s%s</svg>'
           % (x, y, w, h, w * SCALE, h * SCALE, defs, inner))
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode()))).convert('RGBA')


meta = {'scale': SCALE, 'radius': 33, 'wheels': [[124, 410], [390, 410]], 'layers': {}}
# Frame-space layers, cropped to their content; the offset is in frame pixels.
for name in ('shadow', 'arches', 'body'):
    image = raster(group(name), 0, 0, 512, 512)
    left, top, right, bottom = image.getbbox()
    left, top = left // SCALE * SCALE, top // SCALE * SCALE
    image.crop((left, top, right, bottom)).save(os.path.join(OUT, name + '.png'), optimize=True)
    meta['layers'][name] = {'x': left / SCALE, 'y': top / SCALE}
# Wheel layers are centred on the hub.
for name in ('tyre', 'brake', 'rim', 'wheelshade'):
    raster(group(name), -36, -36, 72, 72).save(os.path.join(OUT, name + '.png'), optimize=True)
    meta['layers'][name] = {'x': -36, 'y': -36}
json.dump(meta, open(os.path.join(OUT, 'model.json'), 'w'), indent=1)
print('Rendered', ', '.join(meta['layers']))
