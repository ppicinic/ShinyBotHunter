// Spike demo: Recent, Admin and one milestone window on sample data, in each theme. No backend, no emulator.
const path = require('path'), fs = require('fs'), http = require('http')
const { _electron } = require('D:/Code/ShinyBotHunter/ShinyBotHunter.Qa/dashboard/node_modules/@playwright/test')
const dash = 'D:/Code/ShinyBotHunter-alive/PokemonDashboard'
const out = path.join(__dirname, 'shots3')
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

const GAME = 'HGSS', PHASE = 'Johto Starters and Friends'
// [name, speciesId, formCode, pinned form, has female form, amount, encounters, shinies caught]
const rows = [
  ['totodile', 158, 'totodile', false, false, 1, 4211, 1],
  ['meganium', 154, 'meganium;f', true, true, 1, 1830, 0],
  ['pikachu', 25, 'pikachu;f', true, true, 1, 977, 0],
  ['pikachu', 25, 'pikachu', true, true, 1, 1012, 1],
  ['unfezant', 521, 'unfezant;f', true, true, 1, 310, 0],
  ['aipom', 190, 'aipom', false, true, 2, 6120, 2],
  ['charizard', 6, 'charizard', false, false, 1, 88, 0],
  ['sprigatito', 906, 'sprigatito', false, false, 1, 45, 0],
  ['iron-boulder', 1022, 'iron-boulder', false, false, 1, 12, 0],
  ['wailord', 321, 'wailord', false, false, 0, 530, 0]
]
const counts = {}
for (const [, id, form, pinned, , , enc, caught] of rows) counts[pinned ? form : `#${id}`] = { encounters: enc, shinyEncounters: caught, shiniesCaught: caught }
const all = { encounters: rows.reduce((a, r) => a + r[6], 0), shinyEncounters: 4, shiniesCaught: 4 }
const data = { allPokemonCounts: all, pokemonEncounters: counts, phaseEncounters: { [PHASE]: { allPokemonCounts: all, pokemonEncounters: counts, phaseCount: 1337 } }, caughtPokemons: [], latestEncounters: [], gameSpecificResponse: {} }
const phaseInfo = { gameName: GAME, name: PHASE, isStatic: false, pokemon: rows.map(([name, speciesId, formCode, requireSpecificForm, hasFemaleForm, amount]) => ({ name, speciesId, amount, formCode, requireSpecificForm, hasFemaleForm })) }

const server = http.createServer((req, res) => {
  res.setHeader('Content-Type', 'application/json')
  if (req.url.includes('/encounterRate/')) return res.end('412')
  if (req.url.startsWith('/api/phaseinfo/')) return res.end(JSON.stringify({ phaseInfo, data }))
  res.statusCode = 404
  res.end('{}')
}).listen(5099)

const override = path.join(dash, 'override-config.json')
;(async () => {
  fs.mkdirSync(out, { recursive: true })
  fs.writeFileSync(override, JSON.stringify({ apiUrl: 'http://localhost:5099', backendUrl: 'http://localhost:5098' }))
  const env = { ...process.env, NODE_ENV: 'development' }
  delete env.ELECTRON_RENDERER_URL
  const app = await _electron.launch({
    executablePath: path.join(dash, 'node_modules/electron/dist/electron.exe'),
    args: [dash, '--user-data-dir=' + path.join(__dirname, 'ud')], cwd: dash, env,
    recordVideo: { dir: path.join(out, 'video'), size: { width: 640, height: 700 } }
  })
  try {
    await sleep(6000)
    // The main process opens milestone windows from SignalR; with no backend, open one the same way by hand.
    await app.evaluate(({ BrowserWindow }, q) => {
      const first = BrowserWindow.getAllWindows()[0]
      const file = decodeURIComponent(new URL(first.webContents.getURL()).pathname).replace(/^\//, '')
      const win = new BrowserWindow({ width: 640, height: 700, frame: false, webPreferences: { preload: file.replace('renderer/index.html', 'preload/index.js') } })
      win.loadFile(file, { query: q })
    }, { card: 'phase', game: GAME, phase: PHASE })
    await sleep(3000)
    for (const p of app.windows()) p.on('console', (m) => console.log('console', m.type(), m.text().slice(0, 300)))
    const send = (ch, payload) => app.evaluate(({ BrowserWindow }, { ch, payload }) => BrowserWindow.getAllWindows().forEach((w) => w.webContents.send(ch, payload)), { ch, payload })
    await send('signalr-dashboard-config', { gamePhases: [{ game: GAME, phase: PHASE }], recentEncounterCount: 6 })
    await sleep(2500)
    const byCard = (c) => app.windows().find((p) => p.url().includes('card=' + c))
    const recent = byCard('recent'), admin = byCard('admin'), phase = byCard('phase')
    const mons = [['pikachu', 0], ['pikachu;f', 1], ['unfezant;f', 0], ['totodile', 1], ['sprigatito', 0], ['iron-boulder', 0]]
    const mk = ([formCode, shiny], i) => ({ pid: 1000 + i, formCode, name: formCode.replace(';f', ''), isShiny: !!shiny, gender: formCode.endsWith(';f') ? 2 : 1, level: 5 + i, nature: i, shinyValue: shiny ? 3 : 4000 + i, ivHp: 31, ivAtk: 12, ivDef: 20, ivSpAtk: 7, ivSpDef: 31, ivSpe: 15 })
    console.log('phase api', await phase.evaluate(async () => [typeof window.api, JSON.stringify(await window.api.loadConfig()), (await window.api.request('GET', 'http://localhost:5099/api/phaseinfo/HGSS/x')).status]).catch((e) => String(e)))
    const sel = admin.locator('select[aria-label="Theme"]')
    await sel.selectOption('midnight')
    for (let n = 1; n <= mons.length; n++) {
      await send('signalr-encounters', { response: { ...data, latestEncounters: mons.slice(0, n).map(mk).reverse() }, game: GAME, phase: PHASE })
      await sleep(1500)
    }
    for (const t of ['midnight', 'hgss', 'rse', 'christmas']) {
      await sel.selectOption(t)
      await sleep(2500)
      await phase.screenshot({ path: path.join(out, `${t}-milestone.png`) })
      await recent.screenshot({ path: path.join(out, `${t}-recent.png`) })
    }
    console.log(await phase.evaluate(() => [...document.querySelectorAll('.sprite-stage img')].map((i) => i.src.split('/').slice(-3).join('/') + ' ' + i.className)))
    await sleep(2000)
  } finally {
    await app.close()
    fs.unlinkSync(override)
    server.close()
  }
})().catch((e) => { console.error(e); process.exit(1) })
