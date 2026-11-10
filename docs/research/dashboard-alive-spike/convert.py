"""Wikidex's Scarlet/Violet model clips (GIF or WebM with alpha) -> small transparent GIFs sized like Showdown's."""
import os, re, subprocess, sys
H = {'iron-treads': 70, 'iron-bundle': 85, 'iron-jugulis': 110, 'iron-leaves': 110, 'iron-boulder': 100, 'iron-crown': 105,
     'pecharunt': 60, 'ogerpon': 85, 'ogerpon-wellspring-mask': 85, 'ogerpon-hearthflame-mask': 85, 'ogerpon-cornerstone-mask': 85}
os.makedirs('out', exist_ok=True)
for f in sorted(os.listdir('.')):
    m = re.match(r'(.+)\.([ns])\.(gif|webm)$', f)
    if not m or m.group(1) not in H: continue
    slug, kind, ext = m.groups()
    dec = ['-c:v', 'libvpx-vp9'] if ext == 'webm' else []
    # the union of the opaque area over every frame: cropdetect on the alpha plane keeps a running box
    r = subprocess.run(['ffmpeg', '-hide_banner', *dec, '-i', f, '-vf', 'alphaextract,cropdetect=limit=24:round=2:reset=0', '-f', 'null', '-'], capture_output=True, text=True)
    crops = re.findall(r'crop=(\d+:\d+:\d+:\d+)', r.stderr)
    if not crops: print('no crop', f, r.stderr[-300:]); continue
    crop = crops[-1]
    out = f'out/{slug}.{kind}.gif'
    vf = f"crop={crop},fps=20,scale=-2:{H[slug]}:flags=lanczos,split[a][b];[a]palettegen=reserve_transparent=1:max_colors=127[p];[b][p]paletteuse=alpha_threshold=110:dither=none"
    r = subprocess.run(['ffmpeg', '-hide_banner', '-y', *dec, '-i', f, '-filter_complex', vf, '-loop', '0', out], capture_output=True, text=True)
    print(f, crop, '->', os.path.getsize(out) if os.path.exists(out) else r.stderr[-300:])
