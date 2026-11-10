"""Fill the gaps left by fetch.py: forms that look like their base form, and Alcremie from Showdown's own site."""
import json, os, re, shutil, urllib.request
ROOT = 'D:/Sprites'; OUT = ROOT + '/Animated'
listing = {d: set(re.findall(r'href="\./([^"]+)\.gif', open(f'idx_{d}.html', encoding='utf-8').read())) for d in ('ani', 'ani-shiny')}
# a form whose sprite is its base form's (the default form under its own name, or a form that differs only in data)
ALIAS = {'flabebe-red': 'flabebe', 'floette-red': 'floette', 'florges-red': 'florges', 'furfrou-natural': 'furfrou',
         'vivillon-meadow': 'vivillon', 'gimmighoul-chest': 'gimmighoul', 'sinistea-phony': 'sinistea',
         'poltchageist-counterfeit': 'poltchageist', 'sinistcha-unremarkable': 'sinistcha',
         'koraidon-apex-build': 'koraidon', 'miraidon-ultimate-mode': 'miraidon', 'arceus-unknown': 'arceus',
         'maushold-family-of-three': 'maushold'}
SD = {'furfrou-la-reine': 'furfrou-lareine'}
done = {'alias': [], 'showdown': [], 'none': []}
def get(url, path):
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    open(path, 'wb').write(urllib.request.urlopen(req, timeout=30).read())
for d, sdir in (('Default', 'ani'), ('Shiny', 'ani-shiny')):
    for f in sorted(os.listdir(f'{ROOT}/{d}')):
        slug = f[:-4]; dst = f'{OUT}/{d}/{slug}.gif'
        if os.path.exists(dst): continue
        if slug in ALIAS:
            src = f'{OUT}/{d}/{ALIAS[slug]}.gif'
            if not os.path.exists(src):
                # the base name itself may be a default-form name locally (vivillon -> any vivillon file)
                c = [x for x in os.listdir(f'{OUT}/{d}') if x.startswith(ALIAS[slug])]
                src = f'{OUT}/{d}/{c[0]}' if c else None
            if src: shutil.copyfile(src, dst); done['alias'].append(d + '/' + slug); continue
        cands = []
        if slug in SD: cands.append(SD[slug])
        if slug.startswith('alcremie-') and slug.endswith('-sweet'):
            core = slug[:-len('-sweet')]
            if core.endswith('-strawberry'):
                flavor = core[len('alcremie-'):-len('-strawberry')]
                cands += ['alcremie-' + flavor, 'alcremie-' + flavor.replace('-', '')]
            cands.append(core)
        hit = next((c for c in cands if c in listing[sdir]), None)
        if hit:
            get(f'https://play.pokemonshowdown.com/sprites/{sdir}/{hit}.gif', dst); done['showdown'].append(d + '/' + slug)
        else:
            done['none'].append(d + '/' + slug)
print({k: len(v) for k, v in done.items()})
print([x for x in done['none'] if x.startswith('Default')])
for d in ('Default', 'Shiny', 'Female_Default', 'Female_Shiny'):
    print(d, len(os.listdir(f'{ROOT}/{d}')), len(os.listdir(f'{OUT}/{d}')))
json.dump(done, open('fill_report.json', 'w'), indent=1)
