// Measures what each dashboard animation costs on the built dashboard: every CSS animation off against as built.
// See docs/specs/dashboard-animation-cost.md. Run: node measure-animations.js [row filter] [--samples=N] [--animations=low|medium|high]
const { spawn, execFileSync } = require('child_process')
const fs = require('fs')
const os = require('os')
const path = require('path')

const dash = path.resolve(__dirname, '..', '..', 'PokemonDashboard')
if (!fs.existsSync(path.join(dash, 'out', 'main', 'index.js'))) {
  console.error('build the dashboard first: cd PokemonDashboard; npm run build')
  process.exit(1)
}
const port = 9351
const SETTLE_MS = 3000
const SAMPLE_S = 10
const SAMPLES = Number((process.argv.find((a) => a.startsWith('--samples=')) || '--samples=3').slice(10))
const filter = process.argv.slice(2).find((a) => !a.startsWith('--'))
// As built means every kind of theme animation at this setting: low or medium (the timers) or high (the CSS
// animations). Still is high with every CSS animation off: drawn, not moving, and no timer.
const MODE = (process.argv.find((a) => a.startsWith('--animations=')) || '--animations=low').slice(13)
const STILL = '*,*::before,*::after{animation:none!important}'

const stills = ['eevee', 'chansey', 'rattata', 'hoppip', 'spearow', 'zubat'].map((n) => ({ src: `sprite://local/Shiny/${n}.png`, star: true }))
const gifs = ['wailord', 'joltik', 'totodile', 'pikachu', 'hoppip', 'spearow'].map((n) => ({ src: `sprite://local/Animated/Default/${n}.gif`, star: false }))
const gifStars = gifs.map((g) => ({ src: g.src.replace('/Default/', '/Shiny/'), star: true }))

const lines = [
  ['Nothing (Midnight)', 'midnight', null],
  ['HGSS Gold and Silver', 'hgss-drift', null],
  ['HGSS Dusk', 'hgss-dusk', null],
  ['RS', 'rs-drift', null],
  ['Ruby', 'ruby', null],
  ['Sapphire', 'sapphire', null],
  ['Christmas Pine', 'christmas', null],
  ['Christmas Night', 'christmas-night', null],
  ['Stars on 6 shiny stills (Midnight)', 'midnight', stills],
  ['6 GIF sprites (Midnight)', 'midnight', gifs],
  ['Everything (Christmas Night, 6 shiny GIFs)', 'christmas-night', gifStars]
].filter(([name]) => !filter || name.toLowerCase().includes(filter.toLowerCase()))

const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'sbh-measure-'))
const env = { ...process.env, NODE_ENV: 'development' }
delete env.ELECTRON_RUN_AS_NODE
delete env.ELECTRON_RENDERER_URL
const child = spawn(path.join(dash, 'node_modules', 'electron', 'dist', 'electron.exe'),
  [dash, `--user-data-dir=${userData}`, `--remote-debugging-port=${port}`, '--disable-features=CalculateNativeWinOcclusion'],
  { cwd: dash, stdio: 'ignore', env })

let cleaned = false
function cleanup() {
  if (cleaned) return
  cleaned = true
  try { execFileSync('taskkill', ['/pid', String(child.pid), '/t', '/f'], { stdio: 'ignore' }) } catch { /* already gone */ }
  fs.rmSync(userData, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 })
}
process.on('SIGINT', () => { cleanup(); process.exit(130) })

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const ps = (cmd) => execFileSync('powershell', ['-NoProfile', '-Command', cmd], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })

// CPU seconds so far and process type (main, renderer, gpu, other) of the dashboard's own processes, by pid.
function cpuSeconds() {
  const rows = JSON.parse(ps("Get-CimInstance Win32_Process -Filter \"Name='electron.exe'\" | Select-Object ProcessId,ParentProcessId,KernelModeTime,UserModeTime,CommandLine | ConvertTo-Json -Compress"))
  const out = {}
  for (const r of [rows].flat()) {
    if (r.ProcessId !== child.pid && r.ParentProcessId !== child.pid) continue
    const type = r.ProcessId === child.pid ? 'main' : /--type=renderer/.test(r.CommandLine) ? 'renderer' : /--type=gpu-process/.test(r.CommandLine) ? 'gpu' : 'other'
    out[r.ProcessId] = { s: (Number(r.KernelModeTime) + Number(r.UserModeTime)) / 1e7, type }
  }
  return out
}

function gpuUse(pids) {
  const re = pids.map((p) => `pid_${p}_`).join('|')
  const cmd = `$s = Get-Counter '\\GPU Engine(*)\\Utilization Percentage' -SampleInterval 2 -MaxSamples ${Math.round(SAMPLE_S / 2)}; ` +
    `($s | ForEach-Object { ($_.CounterSamples | Where-Object { $_.InstanceName -match '${re}' } | Measure-Object CookedValue -Sum).Sum } | Measure-Object -Average).Average`
  return Number(ps(cmd).trim()) || 0
}

// One CPU and GPU sample over SAMPLE_S, after SETTLE_MS.
async function sample() {
  await sleep(SETTLE_MS)
  const before = cpuSeconds()
  const t0 = Date.now()
  const gpu = gpuUse(Object.keys(before))
  const after = cpuSeconds()
  const secs = (Date.now() - t0) / 1000
  const byType = { renderer: 0, gpu: 0, main: 0, other: 0 }
  for (const [pid, p] of Object.entries(after)) if (before[pid] !== undefined) byType[p.type] += ((p.s - before[pid].s) / secs) * 100
  return { cpu: byType.renderer + byType.gpu + byType.main + byType.other, gpu, byType }
}

const median = (xs) => [...xs].sort((a, b) => a - b)[Math.floor(xs.length / 2)]
const spread = (xs) => `${median(xs).toFixed(1)} (${Math.min(...xs).toFixed(1)}-${Math.max(...xs).toFixed(1)})`

const probe = `window.__probe = (theme, css, sprites, animations) => {
  document.documentElement.dataset.theme = theme
  Object.assign(document.documentElement.dataset, { animGradients: animations, animSnow: animations, animStars: animations })
  let st = document.getElementById('probe-style')
  if (!st) { st = document.createElement('style'); st.id = 'probe-style'; document.head.appendChild(st) }
  st.textContent = css
  const old = document.getElementById('probe-box'); if (old) old.remove()
  if (sprites) {
    const box = document.createElement('div'); box.id = 'probe-box'
    box.style.cssText = 'position:fixed;left:8px;top:48px;display:flex;flex-wrap:wrap;gap:8px;z-index:40'
    for (const s of sprites) {
      const d = document.createElement('div'); d.className = 'sprite-stage'; d.style.width = '96px'; d.style.height = '96px'
      d.innerHTML = '<img class="pokemon-sprite" src="' + s.src + '">' + (s.star ? '<span class="sprite-sparkle"></span>' : '')
      box.appendChild(d)
    }
    document.body.appendChild(box)
  }
}; 'ok'`
const report = `({ title: document.title.replace('Pokemon Dashboard — ', ''), vis: document.visibilityState, size: innerWidth + 'x' + innerHeight,
  running: document.getAnimations().filter(a => a.playState === 'running').length,
  imgs: [...document.querySelectorAll('#probe-box img')].filter(i => i.naturalWidth > 0).length })`

async function connect(target) {
  const ws = new WebSocket(target.webSocketDebuggerUrl)
  await new Promise((r) => (ws.onopen = r))
  let id = 0
  const pending = new Map()
  ws.onmessage = (m) => { const d = JSON.parse(m.data); if (pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id) } }
  return (expression) => new Promise((r) => { pending.set(++id, r); ws.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression, returnByValue: true } })) })
    .then((d) => d.result.result.value)
}

const pageTargets = async () => (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).filter((t) => t.type === 'page')

async function main() {
  let targets = []
  for (const end = Date.now() + 20000; !targets.length; await sleep(500)) {
    if (Date.now() > end) throw new Error(`no dashboard page on http://127.0.0.1:${port}/json within 20 s`)
    try { targets = await pageTargets() } catch { /* not up yet */ }
  }
  await sleep(8000) // the other windows open after the first
  const pages = []
  for (const t of await pageTargets()) {
    const run = await connect(t)
    await run(probe)
    pages.push(run)
  }

  const set = async (theme, css, sprites) => {
    const animations = css === STILL ? 'high' : MODE
    for (const run of pages) await run(`window.__probe(${JSON.stringify(theme)}, ${JSON.stringify(css)}, ${JSON.stringify(sprites)}, ${JSON.stringify(animations)})`)
  }
  const check = async (name, sprites) => {
    const states = []
    for (const run of pages) states.push(await run(report))
    for (const s of states) {
      if (s.vis !== 'visible') throw new Error(`${name}: window "${s.title}" is ${s.vis}, so its animations are throttled; keep every window on screen`)
      if (sprites && s.imgs < sprites.length) throw new Error(`${name}: window "${s.title}" loaded ${s.imgs} of ${sprites.length} sprites; check spritesRootPath and the Shiny and Animated folders`)
    }
    return states
  }

  const first = await check('start', null)
  console.log(`windows: ${first.map((s) => `${s.title} ${s.size}`).join(', ')}; ${SAMPLES} samples per cell of ${SAMPLE_S} s, each after ${SETTLE_MS / 1000} s settle, still and as built (animations ${MODE}) alternating`)
  console.log("CPU is percent of one core, the dashboard's own processes; GPU is the sum of its GPU engine use; each cell is median (min-max)")
  console.log('built r/g/m/o splits the median as-built sample by process: renderer, gpu, main, other')
  const w = Math.max(...lines.map(([name]) => name.length))
  const row = (cells) => console.log(cells.map((c, i) => (i ? String(c).padStart(i === 6 ? 7 : 18) : c.padEnd(w))).join(' | '))
  row(['What', 'CPU still', 'CPU as built', 'GPU still', 'GPU as built', 'built r/g/m/o', 'running'])
  for (const [name, theme, sprites] of lines) {
    const still = []
    const built = []
    let running = ''
    for (let i = 0; i < SAMPLES; i++) {
      await set(theme, STILL, sprites)
      still.push(await sample())
      await check(name, sprites)
      await set(theme, '', sprites)
      built.push(await sample())
      running = (await check(name, sprites)).map((s) => s.running).join('+')
    }
    const mid = built.find((b) => b.cpu === median(built.map((x) => x.cpu)))
    const split = Object.values(mid.byType).map((c) => c.toFixed(1)).join('/')
    row([name, spread(still.map((x) => x.cpu)), spread(built.map((x) => x.cpu)), spread(still.map((x) => x.gpu)), spread(built.map((x) => x.gpu)), split, running])
  }
}

main().catch((e) => { console.error(e.message); process.exitCode = 1 }).finally(cleanup)
