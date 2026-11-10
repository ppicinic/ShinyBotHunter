import json, os, re
exec(open('cov.py').read().split("res={}")[0])
dex = json.load(open('sd_dex.json', encoding='utf-8'))
tid = lambda s: re.sub(r'[^a-z0-9]', '', s.lower())
def listing(d):
    return set(re.findall(r'href="\./([^"]+)\.(?:gif|png)', open(f'idx_{d}.html', encoding='utf-8').read()))
ani, anis, g5, g5s = listing('gen5ani'), listing('gen5ani-shiny'), listing('gen5'), listing('gen5-shiny')
def sid(e):
    b = tid(e.get('baseSpecies', e['name']))
    f = e.get('forme')
    return b + ('-' + tid(f) if f else '')
def sd_entry(slug):
    k = tid(slug)
    if k in dex: return dex[k]
    # default-form names (deoxys-normal) and names Showdown spells differently: fall back to the species
    parts = slug.split('-')
    for n in range(len(parts) - 1, 0, -1):
        k = tid(''.join(parts[:n]))
        if k in dex: return ('species', dex[k])
    return None
rows = []
for slug in sorted(f[:-4] for f in os.listdir('D:/Sprites/Default')):
    ks_, best = keys(slug)
    if not best: continue
    g = gen(best[1]); exact = ks_[:-1]
    bw = any(x in src['bw'][''] for x in exact)
    e = sd_entry(slug); exactsd = e is not None and not isinstance(e, tuple)
    s = sid(e) if exactsd else None
    rows.append((g, slug, bw, bool(s and s in ani), bool(s and s in g5), exactsd))
print('gen  forms  bwAni  sdGen5Ani  either  neither  (neither but gen5-style still on Showdown)')
left = []
for g in range(1, 10):
    r = [x for x in rows if x[0] == g]
    either = [x for x in r if x[2] or x[3]]
    neither = [x for x in r if not (x[2] or x[3])]
    print(g, len(r), sum(x[2] for x in r), sum(x[3] for x in r), len(either), len(neither), sum(x[4] for x in neither))
    left += neither
json.dump([(g, s, st, ex) for g, s, _, _, st, ex in left], open('left.json', 'w'))
for g in range(1, 10):
    names = [s for gg, s, *_ in left if gg == g and 'alcremie' not in s]
    print(g, len(names), ' '.join(names)[:1500])
# base species only (dex number, no forms)
base_left = {}
for g, slug, bw, a, st, ex in rows:
    if slug in base and not (bw or a): base_left.setdefault(g, []).append(slug)
print('base species with no pixel animation:', {g: len(v) for g, v in base_left.items()})
fem = [f[:-4] for f in os.listdir('D:/Sprites/Female_Default')]
print('female -f in gen5ani:', sum(1 for x in ani if x.endswith('-f')), 'gen5:', sum(1 for x in g5 if x.endswith('-f')))
