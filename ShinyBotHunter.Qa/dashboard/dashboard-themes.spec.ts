import { spawnSync } from 'child_process'
import * as fs from 'fs'
import * as http from 'http'
import * as path from 'path'
import { _electron, expect, test, type ElectronApplication, type Page } from '@playwright/test'

// No emulator and no backend: a stub answers the two REST calls and the SignalR pushes are sent by hand.
// driver.ts is not imported, because importing it creates an hgss-route33 run folder.
const repoDir = path.resolve(__dirname, '..', '..')
const baseRef = process.env.THEMES_BASE_REF ?? 'origin/main'
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15)
const runDir = path.join(__dirname, 'qa-runs', `${stamp}-dashboard-themes`)
const THEMES = ['hgss-drift', 'hgss-dusk', 'rs-drift', 'ruby', 'sapphire', 'christmas', 'christmas-night']
const WINDOWS = ['recent', 'admin', 'milestone'] as const
const SIZES: Record<string, [number, number]> = { admin: [820, 860], recent: [480, 700], phase: [640, 700] }
const GAME = 'HGSS'
const PHASE = 'Johto Starters and Friends'

function step(message: string): void {
  const line = `${new Date().toISOString()} ${message}`
  console.log(line)
  fs.appendFileSync(path.join(runDir, 'steps.log'), line + '\n')
}

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

function run(command: string, cwd: string, log: string): void {
  const result = spawnSync(command, { cwd, shell: true, encoding: 'utf8' })
  fs.appendFileSync(path.join(runDir, log), `> ${command}\n${result.stdout}${result.stderr}\n`)
  expect(result.status, `"${command}" in ${cwd} failed; see ${log}`).toBe(0)
}

/** Extracts PokemonDashboard/ at `ref` into runDir/<name> and builds it against the stub. */
function prepare(name: string, ref: string): string {
  const sha = spawnSync('git', ['rev-parse', ref], { cwd: repoDir, encoding: 'utf8' }).stdout.trim()
  step(`${name}: ${ref} is ${sha}`)
  const dir = path.join(runDir, name)
  fs.mkdirSync(dir)
  // Relative tar paths: tar reads a drive letter's colon as a remote host.
  run(`git archive --format=tar -o "${path.join(runDir, name + '.tar')}" ${sha} PokemonDashboard`, repoDir, `${name}-build.log`)
  run(`tar -xf ../${name}.tar`, dir, `${name}-build.log`)
  const dashboard = path.join(dir, 'PokemonDashboard')
  fs.writeFileSync(path.join(dashboard, 'override-config.json'), JSON.stringify({
    apiUrl: 'http://localhost:5099',
    backendUrl: 'http://localhost:5098',
    spritesRootPath: path.join(runDir, 'no-sprites')
  }))
  run('npm ci', dashboard, `${name}-build.log`)
  run('npm run build', dashboard, `${name}-build.log`)
  step(`${name}: built`)
  return dashboard
}

// The stub's data, as the spike's demo3.js: [name, speciesId, formCode, pinned form, has female form, amount, encounters, caught].
const rows: [string, number, string, boolean, boolean, number, number, number][] = [
  ['totodile', 158, 'totodile', false, false, 1, 4211, 1],
  ['meganium', 154, 'meganium;f', true, true, 1, 1830, 0],
  ['pikachu', 25, 'pikachu;f', true, true, 1, 977, 0],
  ['pikachu', 25, 'pikachu', true, true, 1, 1012, 1],
  ['aipom', 190, 'aipom', false, true, 2, 6120, 2],
  ['wailord', 321, 'wailord', false, false, 0, 530, 0]
]
const counts = Object.fromEntries(rows.map(([, id, form, pinned, , , enc, caught]) =>
  [pinned ? form : `#${id}`, { encounters: enc, shinyEncounters: caught, shiniesCaught: caught }]))
const all = { encounters: rows.reduce((a, r) => a + r[6], 0), shinyEncounters: 4, shiniesCaught: 4 }
const data = { allPokemonCounts: all, pokemonEncounters: counts, phaseEncounters: { [PHASE]: { allPokemonCounts: all, pokemonEncounters: counts, phaseCount: 1337 } }, caughtPokemons: [], latestEncounters: [], gameSpecificResponse: {} }
const phaseInfo = { gameName: GAME, name: PHASE, isStatic: false, pokemon: rows.map(([name, speciesId, formCode, requireSpecificForm, hasFemaleForm, amount]) => ({ name, speciesId, amount, formCode, requireSpecificForm, hasFemaleForm })) }
const mons: [string, boolean][] = [['pikachu', false], ['pikachu;f', true], ['meganium;f', false], ['totodile', false], ['aipom', false], ['wailord', false]]
const encounter = ([formCode, shiny]: [string, boolean], i: number): object => ({
  pid: 1000 + i, formCode, name: formCode.replace(';f', ''), isShiny: shiny, gender: formCode.endsWith(';f') ? 2 : 1, level: 5 + i, nature: i,
  shinyValue: shiny ? 3 : 4000 + i, ivHp: 31, ivAtk: 12, ivDef: 20, ivSpAtk: 7, ivSpDef: 31, ivSpe: 15
})

/** Launches one build, opens a milestone window by hand, pushes the config and six encounters, opens the Window Layout tab. */
async function launch(dashboard: string, name: string, video: boolean): Promise<[ElectronApplication, Record<string, Page>]> {
  const env: Record<string, string> = { ...(process.env as Record<string, string>), NODE_ENV: 'development' }
  delete env.ELECTRON_RENDERER_URL
  const app = await _electron.launch({
    executablePath: path.join(dashboard, 'node_modules', 'electron', 'dist', 'electron.exe'),
    args: [dashboard, `--user-data-dir=${path.join(runDir, name + '-userdata')}`],
    cwd: dashboard,
    env,
    ...(video ? { recordVideo: { dir: path.join(runDir, 'video'), size: { width: 820, height: 860 } } } : {})
  })
  await sleep(6000)
  await app.evaluate(({ BrowserWindow }, query) => {
    const file = decodeURIComponent(new URL(BrowserWindow.getAllWindows()[0].webContents.getURL()).pathname).replace(/^\//, '')
    const win = new BrowserWindow({ width: 640, height: 700, frame: false, webPreferences: { preload: file.replace('renderer/index.html', 'preload/index.js') } })
    win.loadFile(file, { query })
  }, { card: 'phase', game: GAME, phase: PHASE })
  await sleep(3000)
  await app.evaluate(({ BrowserWindow }, sizes) => BrowserWindow.getAllWindows().forEach((w) => {
    const card = new URL(w.webContents.getURL()).searchParams.get('card') ?? ''
    w.setContentSize(...(sizes[card] as [number, number]))
  }), SIZES)
  const send = (channel: string, payload: object): Promise<void> => app.evaluate(({ BrowserWindow }, { channel, payload }) =>
    BrowserWindow.getAllWindows().forEach((w) => w.webContents.send(channel, payload)), { channel, payload })
  await send('signalr-dashboard-config', { gamePhases: [{ game: GAME, phase: PHASE, gameName: null }], recentEncounterCount: 6 })
  await sleep(2500)
  for (let n = 1; n <= mons.length; n++) {
    await send('signalr-encounters', { response: { ...data, latestEncounters: mons.slice(0, n).map(encounter).reverse() }, game: GAME, phase: PHASE })
    await sleep(1000)
  }
  const byCard = (card: string): Page => app.windows().find((p) => p.url().includes(`card=${card}`))!
  const pages = { recent: byCard('recent'), admin: byCard('admin'), milestone: byCard('phase') }
  // Not Dashboard Config: A4b adds the Appearance section there, which main does not have.
  await pages.admin.getByRole('button', { name: 'Window Layout', exact: true }).click()
  await sleep(1500)
  step(`${name}: windows ready`)
  return [app, pages]
}

// --- contrast (WCAG 2.x) -------------------------------------------------------------------------

type Rgb = [number, number, number]
const triplet = (s: string): Rgb => s.trim().split(/\s+/).map(Number) as Rgb
const hex = (h: string): Rgb => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)) as Rgb
const luminance = (c: Rgb): number => {
  const [r, g, b] = c.map((v) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4 })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
const ratio = (a: Rgb, b: Rgb): number => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}
const show = (c: Rgb): string => c.join(' ')
/** Splits at commas outside parentheses. */
const layers = (s: string): string[] => {
  const out = ['']
  let depth = 0
  for (const ch of s) {
    if (ch === ',' && depth === 0) { out.push(''); continue }
    if (ch === '(') depth++
    if (ch === ')') depth--
    out[out.length - 1] += ch
  }
  return out.map((l) => l.trim())
}

async function contrastRows(page: Page, theme: string): Promise<string[]> {
  const vars = await page.evaluate(() => {
    const s = getComputedStyle(document.documentElement)
    return Object.fromEntries(['--ink', '--accent-500', '--accent-600', '--accent-700', '--header-title', '--n-400', '--card-header', '--header-fill', '--header-size']
      .map((v) => [v, s.getPropertyValue(v).trim()]))
  })
  const ink = triplet(vars['--ink'])
  const title = triplet(vars['--header-title'] || vars['--n-400'])
  const out: string[] = []
  const row = (pair: string, fg: Rgb, bg: Rgb, note = ''): void => {
    out.push(`| ${theme} | ${pair} | ${show(fg)} | ${show(bg)} | ${ratio(fg, bg).toFixed(2)} | ${note} |`)
  }
  for (const shade of ['500', '600', '700']) row(`ink on accent-${shade}`, ink, triplet(vars[`--accent-${shade}`]))
  row('title on card-header', title, triplet(vars['--card-header']))
  const sizes = layers(vars['--header-size'])
  layers(vars['--header-fill']).forEach((layer, i) => {
    const underline = /\s[1-4]px$/.test(sizes[i] ?? '') ? 'underline layer, under no text' : ''
    for (const token of layer.match(/#[0-9a-f]{6}|rgb\([^)]*\)/gi) ?? []) {
      const bg = token.startsWith('#') ? hex(token) : (token.slice(4, -1).split(/[\s,/]+/).slice(0, 3).map(Number) as Rgb)
      row(`title on header stop ${token}`, title, bg, underline)
    }
  })
  return out
}

// --- the check -----------------------------------------------------------------------------------

test('dashboard themes: Midnight matches the base, seven themes, contrast, reduced motion', async () => {
  fs.mkdirSync(path.join(runDir, 'no-sprites'), { recursive: true })
  const server = http.createServer((req, res) => {
    res.setHeader('Content-Type', 'application/json')
    if (req.url!.includes('/encounterRate/')) return void res.end('412')
    if (req.url!.startsWith('/api/phaseinfo/')) return void res.end(JSON.stringify({ phaseInfo, data }))
    res.statusCode = 404
    res.end('{}')
  }).listen(5099)
  let app: ElectronApplication | null = null
  try {
    const base = prepare('base', baseRef)
    const tip = prepare('tip', 'HEAD')

    let pages: Record<string, Page>
    ;[app, pages] = await launch(base, 'base', false)
    for (const w of WINDOWS) await pages[w].screenshot({ path: path.join(runDir, `midnight-${w}-base.png`), animations: 'disabled' })
    await app.close()
    app = null

    ;[app, pages] = await launch(tip, 'tip', true)
    const mismatched: string[] = []
    for (const w of WINDOWS) {
      const shot = path.join(runDir, `midnight-${w}.png`)
      await pages[w].screenshot({ path: shot, animations: 'disabled' })
      const baseShot = path.join(runDir, `midnight-${w}-base.png`)
      if (fs.readFileSync(shot).equals(fs.readFileSync(baseShot))) fs.rmSync(baseShot)
      else mismatched.push(w)
    }
    step(mismatched.length ? `Midnight differs from the base on: ${mismatched.join(', ')}` : 'Midnight is byte-equal to the base on all windows')

    const table = ['| Theme | Pair | Foreground | Background | Ratio | Note |', '|---|---|---|---|---|---|', ...(await contrastRows(pages.recent, 'midnight'))]
    for (const theme of THEMES) {
      for (const w of WINDOWS) await pages[w].evaluate((t) => { document.documentElement.dataset.theme = t }, theme)
      await sleep(5000)
      for (const w of WINDOWS) await pages[w].screenshot({ path: path.join(runDir, `${theme}-${w}.png`) })
      table.push(...(await contrastRows(pages.recent, theme)))
      step(`${theme}: screenshots`)
    }
    fs.writeFileSync(path.join(runDir, 'contrast.md'), table.join('\n') + '\n')
    console.log(table.join('\n'))

    const snowLayers = (): Promise<{ content: string[]; pointer: string[]; animation: string[] }> => pages.recent.evaluate(() => {
      const layers = [getComputedStyle(document.body, '::before'), getComputedStyle(document.body, '::after'), getComputedStyle(document.getElementById('root')!, '::after')]
      const animated = [document.querySelector('.dashboard-card')!, document.querySelector('.card-header')!].map((e) => getComputedStyle(e).animationName)
      return { content: layers.map((l) => l.content), pointer: layers.map((l) => l.pointerEvents), animation: animated }
    })
    for (const w of WINDOWS) await pages[w].evaluate(() => { document.documentElement.dataset.theme = 'christmas' })
    const moving = await snowLayers()
    step(`christmas: snow content ${moving.content.join(', ')}; pointer-events ${moving.pointer.join(', ')}`)
    expect(moving.pointer, 'the snow layers take clicks').toEqual(['none', 'none', 'none'])
    await pages.recent.emulateMedia({ reducedMotion: 'reduce' })
    const still = await snowLayers()
    step(`christmas, reduced motion: snow content ${still.content.join(', ')}; animations ${still.animation.join(', ')}`)
    expect(still.content, 'snow under reduced motion').toEqual(['none', 'none', 'none'])
    expect(still.animation, 'card and header animation under reduced motion').toEqual(['none', 'none'])

    expect(mismatched, 'Midnight screenshots that differ from the base (both PNGs kept)').toEqual([])
  } finally {
    if (app) await app.close()
    server.close()
  }
})
