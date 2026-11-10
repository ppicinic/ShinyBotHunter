"""Spike: write the picked theme designs into the prototype's index.css, tailwind.config.js and theme.ts."""
dash = 'D:/Code/ShinyBotHunter-alive/PokemonDashboard/'

def rgb(h):
    h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
def trip(c): return ' '.join(str(round(x)) for x in c)
def mix(a, b, t): return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))
T = {100: .05, 200: .12, 300: .22, 400: .36, 500: .52, 600: .68, 700: .78, 800: .87, 900: .94}
G = lambda stops, deg=120: f'linear-gradient({deg}deg, {stops})'

# id, label, ground, ink, muted-base, border, header colour, accent 500/600/700, accent-2, extras
themes = [
  dict(id='hgss-drift', label='HGSS Gold and Silver', bg='#17171c', ink='#fbf6e9', dark='#1c1b1f', border='#5b5340', header='#8a6a1c',
       acc=('#e3b341', '#94690c', '#7a5608'), acc2='#eef1f5', title='29 22 6', panel='0 0 0', cell='rgb(255 255 255 / 0.08)',
       fill=G('#3a2a06, #17171c 30%, #232a36 55%, #17171c 78%, #3a2a06'), fill_size='400% 400%', fill_anim='theme-drift 36s ease-in-out infinite alternate',
       hdr=G('#b8860b, #f3d57a 22%, #c9952a 40%, #9aa3b0 62%, #eef1f5 80%, #8f98a6', 100), hdr_size='250% 100%', hdr_anim='theme-drift 24s ease-in-out infinite alternate'),
  dict(id='hgss-dusk', label='HGSS Dusk', bg='#131210', ink='#f5f1e6', dark='#1a1814', border='#3a3529', header='#1b1916',
       acc=('#d9a62e', '#94690c', '#7a5608'), acc2='#c9ced6', title='240 214 138', panel='255 255 255', cell='rgb(255 255 255 / 0.07)',
       glow='radial-gradient(closest-side, rgb(227 179 65 / 0.34), transparent), radial-gradient(closest-side, rgb(170 190 220 / 0.28), transparent)', glow_size='70% 70%, 80% 80%',
       hdr=G('#e3b341, #c9ced6', 90) + ' bottom no-repeat, #1b1916', hdr_size='100% 2px, auto'),
  dict(id='rs-drift', label='Ruby and Sapphire', bg='#17161c', ink='#f8f5fb', dark='#1b1a22', border='#4a3f52', header='#5a2a8a',
       acc=('#ff5d78', '#c2183a', '#9c1230'), acc2='#3a7bff', title='255 255 255', panel='0 0 0', cell='rgb(255 255 255 / 0.08)',
       fill=G('#3d0812, #17161c 30%, #101c3e 55%, #17161c 78%, #3d0812'), fill_size='400% 400%', fill_anim='theme-drift 36s ease-in-out infinite alternate',
       hdr=G('#8f0f22, #ff6b81 20%, #c2183a 38%, #5a2a8a 50%, #1f4fd0 62%, #86b6ff 80%, #1238a0', 100), hdr_size='250% 100%', hdr_anim='theme-drift 24s ease-in-out infinite alternate'),
  dict(id='ruby', label='Ruby', bg='#24050b', ink='#fff4f5', dark='#2a0a12', border='#6a2434', header='#7d0c1c',
       acc=('#ff5d78', '#c2183a', '#9c1230'), acc2='#ffc2cc', title='255 255 255', panel='0 0 0', cell='rgb(255 255 255 / 0.08)',
       fill=G('#24050b, #5a0d1c 35%, #3a0a26 65%, #24050b'), fill_size='300% 300%', fill_anim='theme-drift 34s ease-in-out infinite alternate',
       hdr=G('#7d0c1c, #e5364f 30%, #ff8fa3 48%, #c2183a 60%, #7d0c1c', 100), hdr_size='260% 100%', hdr_anim='theme-drift 20s ease-in-out infinite alternate'),
  dict(id='sapphire', label='Sapphire', bg='#050d26', ink='#f2f7ff', dark='#0a1434', border='#24407e', header='#0c2a80',
       acc=('#5aa9ff', '#2563eb', '#1d4ed8'), acc2='#cfe3ff', title='255 255 255', panel='0 0 0', cell='rgb(255 255 255 / 0.08)',
       fill=G('#050d26, #0e2a6a 35%, #0a1a52 65%, #050d26'), fill_size='300% 300%', fill_anim='theme-drift 34s ease-in-out infinite alternate',
       hdr=G('#0c2a80, #2f6fe0 30%, #9cc4ff 48%, #1f4fd0 60%, #0c2a80', 100), hdr_size='260% 100%', hdr_anim='theme-drift 20s ease-in-out infinite alternate'),
  dict(id='christmas', label='Christmas Pine', bg='#0a1c12', ink='#fffaf0', dark='#102418', border='#2c5438', header='#8c1824',
       acc=('#e23d3d', '#c62828', '#9f1d1d'), acc2='#f2c14e', title='255 240 214', panel='0 0 0', cell='rgb(255 255 255 / 0.08)',
       fill=G('#0a1c12, #133021 50%, #0a1c12'), fill_size='250% 250%', fill_anim='theme-drift 30s ease-in-out infinite alternate',
       hdr='repeating-linear-gradient(135deg, #8c1824 0 14px, #78121e 14px 28px)', snow=True),
  dict(id='christmas-night', label='Christmas Night', bg='#081226', ink='#f6f9ff', dark='#0e1a34', border='#27406a', header='#0f3d2a',
       acc=('#f2c14e', '#9a7412', '#7d5e0c'), acc2='#e23d3d', title='255 233 176', panel='255 255 255', cell='rgb(255 255 255 / 0.09)',
       fill=G('#081226, #0e2140 55%, #1a2b3a', 180),
       glow='radial-gradient(closest-side, rgb(242 193 78 / 0.22), transparent)', glow_size='120% 60%', glow_anim='theme-breathe 8s ease-in-out infinite',
       hdr=G('#0f3d2a, #14553a 50%, #0f3d2a', 90), snow=True, lights=True),
]

css = ['']
for t in themes:
    ink, dark = rgb(t['ink']), rgb(t['dark'])
    sel = f":root[data-theme='{t['id']}']"
    L = [f"/* {t['label']} */", sel + ' {',
         f"  --bg: {trip(rgb(t['bg']))};", f"  --card-bg: {trip(rgb(t['bg']))};", f"  --card-header: {trip(rgb(t['header']))};",
         f"  --card-border: {trip(rgb(t['border']))};", f"  --panel: {t['panel']};", f"  --cell: {t['cell']};", f"  --ink: {trip(ink)};",
         '  ' + ' '.join(f"--n-{k}: {trip(mix(ink, dark, v))};" for k, v in T.items()),
         f"  --accent-500: {trip(rgb(t['acc'][0]))}; --accent-600: {trip(rgb(t['acc'][1]))}; --accent-700: {trip(rgb(t['acc'][2]))};",
         f"  --accent-2: {trip(rgb(t['acc2']))};", f"  --scroll-thumb: {trip(rgb(t['border']))}; --scroll-thumb-hover: {trip(mix(rgb(t['border']), ink, .3))};",
         f"  --header-fill: {t['hdr']};", f"  --header-title: {t['title']};"]
    for k, var in (('hdr_size', '--header-size'), ('hdr_anim', '--header-anim'), ('fill', '--card-fill'), ('fill_size', '--card-fill-size'), ('fill_anim', '--card-fill-anim'),
                   ('glow', '--card-glow'), ('glow_size', '--card-glow-size'), ('glow_anim', '--card-glow-anim')):
        if k in t: L.append(f"  {var}: {t[k]};")
    if 'glow' in t and 'glow_anim' not in t: L.append('  --card-glow-anim: theme-orbit 30s ease-in-out infinite;')
    L.append('}')
    css.append('\n'.join(L))
class _S:
    def __mod__(self, w): return ", ".join(f":root[data-theme='{t['id']}'] body::{w}" for t in themes if t.get('snow'))
snow = _S()
css.append('''/* The moving parts: a ground wider than the window that slides, two glows that circle, snow, string lights. */
@keyframes theme-drift { from { background-position: 0% 50%; } to { background-position: 100% 50%; } }
@keyframes theme-orbit {
  0%, 100% { background-position: 0% 0%, 100% 100%; }
  50% { background-position: 60% 40%, 40% 60%; }
}
@keyframes theme-breathe { 0%, 100% { opacity: 0.55; } 50% { opacity: 1; } }
@keyframes theme-snow { from { transform: translate3d(0, 0, 0); } to { transform: translate3d(var(--dx), var(--dy), 0); } }

''' + snow % 'before' + ',\n' + snow % 'after' + ''' {
  content: '';
  position: fixed;
  inset: -320px 0 0 0;
  pointer-events: none;
  z-index: 50;
  animation: theme-snow linear infinite;
}
''' + snow % 'before' + ''' {
  background-image:
    radial-gradient(2px 2px at 23px 41px, #fff, transparent),
    radial-gradient(2px 2px at 151px 97px, rgb(255 255 255 / 0.8), transparent),
    radial-gradient(1.5px 1.5px at 89px 203px, rgb(255 255 255 / 0.7), transparent),
    radial-gradient(2px 2px at 211px 263px, rgb(255 255 255 / 0.9), transparent),
    radial-gradient(1px 1px at 13px 171px, rgb(255 255 255 / 0.6), transparent),
    radial-gradient(1px 1px at 197px 29px, rgb(255 255 255 / 0.5), transparent);
  background-size: 257px 311px;
  animation-duration: 14s;
  --dx: 30px; --dy: 311px;
}
''' + snow % 'after' + ''' {
  background-image:
    radial-gradient(3px 3px at 61px 17px, rgb(255 255 255 / 0.7), transparent),
    radial-gradient(3px 3px at 173px 149px, rgb(255 255 255 / 0.55), transparent),
    radial-gradient(2.5px 2.5px at 7px 131px, rgb(255 255 255 / 0.6), transparent);
  background-size: 199px 233px;
  animation-duration: 9s;
  --dx: -24px; --dy: 233px;
}

:root[data-theme='christmas-night'] .card-header::after {
  content: '';
  position: absolute;
  left: 0; right: 0; bottom: -6px;
  height: 10px;
  pointer-events: none;
  z-index: 2;
  background-image:
    radial-gradient(circle 3px at 10px 5px, #ff4d4d 60%, transparent 62%),
    radial-gradient(circle 3px at 34px 6px, #ffd166 60%, transparent 62%),
    radial-gradient(circle 3px at 58px 5px, #4ade80 60%, transparent 62%),
    radial-gradient(circle 3px at 82px 6px, #60a5fa 60%, transparent 62%);
  background-size: 96px 10px;
  filter: drop-shadow(0 0 4px rgb(255 255 255 / 0.6));
  animation: theme-breathe 2.4s ease-in-out infinite;
}
''')

p = dash + 'src/renderer/src/assets/index.css'
s = open(p, encoding='utf-8').read()
a = s.index('/* HeartGold SoulSilver:'); b = s.index('/* ============================================================\n   Base / Reset')
s = s[:a] + '\n\n'.join(css).lstrip('\n') + '\n' + s[b:]
def rep(a_, b_):
    global s
    assert a_ in s, a_[:60]; s = s.replace(a_, b_, 1)
rep('  --scroll-thumb: 61 61 107;', '  --panel: 45 45 78;\n  --scroll-thumb: 61 61 107;')
rep('''  background-color: rgb(var(--card-bg));
  border: 1px solid rgb(var(--card-border));''', '''  position: relative;
  isolation: isolate;
  background: var(--card-fill, rgb(var(--card-bg)));
  background-size: var(--card-fill-size, auto);
  animation: var(--card-fill-anim, none);
  border: 1px solid rgb(var(--card-border));''')
rep('''.card-header {''', '''.dashboard-card::before {
  content: '';
  position: absolute;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  background: var(--card-glow, none);
  background-size: var(--card-glow-size, auto);
  background-repeat: no-repeat;
  animation: var(--card-glow-anim, none);
}

.card-header {''')
rep('  background: var(--header-fill, rgb(var(--card-header)));', '  position: relative;\n  background: var(--header-fill, rgb(var(--card-header)));\n  background-size: var(--header-size, auto);\n  animation: var(--header-anim, none);')
rep('''  @apply text-center rounded py-1;
  background-color: rgb(var(--card-border));''', '''  @apply text-center rounded py-1;
  background-color: var(--cell, rgb(var(--card-border)));''')
s = s.replace('.sprite-sparkle::after, body::after { animation: none !important; }', '.sprite-sparkle::after, body::before, body::after, .dashboard-card, .dashboard-card::before, .card-header, .card-header::after { animation: none !important; }')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

p = dash + 'tailwind.config.js'
s = open(p, encoding='utf-8').read()
rep("      fontFamily: {", "      // Fills named card-border are the translucent panels; borders keep the border colour.\n      backgroundColor: {\n        'card-border': v('panel'),\n      },\n      fontFamily: {")
open(p, 'w', encoding='utf-8', newline='\n').write(s)

p = dash + 'src/renderer/src/utils/theme.ts'
s = open(p, encoding='utf-8').read()
a = s.index('export const THEMES = ['); b = s.index('] as const')
s = s[:a] + "export const THEMES = [\n  { id: 'midnight', label: 'Midnight' },\n" + ''.join(f"  {{ id: '{t['id']}', label: '{t['label']}' }},\n" for t in themes).rstrip(',\n') + '\n' + s[b:]
open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
