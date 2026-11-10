import { spawn, spawnSync, type ChildProcess } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { _electron, expect, test, type ElectronApplication, type Page } from '@playwright/test'

// No emulator: the real backend and the built dashboard with its own user-data folder; sample encounters are sent
// over the SignalR IPC channel by hand. driver.ts is not imported, because importing it creates an hgss-route33
// run folder and its launchDashboard takes no --user-data-dir; the few pieces needed are copied here.
const repoDir = path.resolve(__dirname, '..', '..')
const dashboardDir = path.join(repoDir, 'PokemonDashboard')
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15)
const runDir = path.join(__dirname, 'qa-runs', `${stamp}-dashboard-alive`)
const userData = path.join(runDir, 'userdata')
const API = 'http://localhost:5000/api'
const DOTTED = 'QA.alive'
// Midnight last, so the loop ends on it.
const THEMES = ['hgss-drift', 'hgss-dusk', 'rs-drift', 'ruby', 'sapphire', 'christmas', 'christmas-night', 'midnight']

function step(message: string): void {
  const line = `${new Date().toISOString()} ${message}`
  console.log(line)
  fs.appendFileSync(path.join(runDir, 'steps.log'), line + '\n')
}

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

function killTree(pid: number): void {
  spawnSync('taskkill', ['/T', '/F', '/PID', String(pid)])
}

async function answers(): Promise<boolean> {
  try {
    return (await fetch(`${API}/getAllGames`)).ok
  } catch {
    return false
  }
}

// Appended, because the backend is started twice in one run.
async function startBackend(): Promise<ChildProcess> {
  const log = fs.openSync(path.join(runDir, 'backend.log'), 'a')
  const proc = spawn('dotnet', ['run', '-c', 'Debug'], { cwd: path.join(repoDir, 'ShinyBotHunter'), stdio: ['pipe', log, log] })
  const deadline = Date.now() + 4 * 60 * 1000
  while (Date.now() < deadline) {
    if (proc.exitCode !== null) throw new Error(`backend exited with ${proc.exitCode}; see backend.log`)
    if (await answers()) {
      step('backend answers on localhost:5000')
      return proc
    }
    await sleep(2000)
  }
  throw new Error('backend did not answer on localhost:5000 within 4 minutes')
}

/** Writes "end" to the backend's stdin and waits up to 60 s; kills the tree if it is still running. Returns whether it exited by itself. */
async function stopBackend(proc: ChildProcess): Promise<boolean> {
  if (proc.exitCode !== null) return true
  const exited = await new Promise<boolean>((resolve) => {
    const timer = setTimeout(() => resolve(false), 60_000)
    proc.once('exit', () => {
      clearTimeout(timer)
      resolve(true)
    })
    proc.stdin!.write('end\n')
  })
  if (!exited) killTree(proc.pid!)
  step(exited ? `backend stopped (exit ${proc.exitCode})` : 'backend ignored "end" for 60 s; killed')
  return exited
}

function launch(): Promise<ElectronApplication> {
  const env: Record<string, string> = { ...(process.env as Record<string, string>), NODE_ENV: 'development' }
  delete env.ELECTRON_RENDERER_URL
  // Every launch uses the same user-data folder, so the window layout and the remembered themes carry over
  // between launches and the owner's own layout is never touched.
  return _electron.launch({
    executablePath: path.join(dashboardDir, 'node_modules', 'electron', 'dist', 'electron.exe'),
    args: [dashboardDir, `--user-data-dir=${userData}`],
    cwd: dashboardDir,
    env,
    recordVideo: { dir: path.join(runDir, 'video'), size: { width: 820, height: 900 } }
  })
}

async function windowByTitle(app: ElectronApplication, title: string, timeoutMs: number): Promise<Page> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    for (const page of app.windows()) {
      if ((await page.title()) === title) return page
    }
    await sleep(500)
  }
  throw new Error(`no window titled "${title}" within ${timeoutMs / 1000} s`)
}

const theme = (page: Page): Promise<string> => page.evaluate(() => document.documentElement.dataset.theme ?? '')
const expectTheme = (page: Page, id: string): Promise<void> => expect.poll(() => theme(page), { timeout: 30_000 }).toBe(id)

// One line from windowContracts.phaseWindowName.
const enc = encodeURIComponent
const phaseName = (game: string, phase: string, gameName: string | null): string =>
  'phase:' + enc(game) + ':' + enc(phase) + ':' + enc(gameName ?? '')

async function post(route: string, body: unknown): Promise<void> {
  const res = await fetch(API + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  expect(res.ok, `POST ${route} returned ${res.status}`).toBe(true)
}

const DEFAULT_APPEARANCE = { dayStart: '07:00', nightStart: '19:00', default: { theme: 'midnight' }, windows: {} }

// The sample encounters, as the spike's demo3.js builds them: [formCode, shiny].
const MONS: [string, boolean][] = [
  ['pikachu', false], // Black/White
  ['pikachu;f', false],
  ['unfezant;f', false],
  ['totodile', true], // shiny, for the twinkle
  ['sprigatito', false], // Showdown
  ['ogerpon', false], // Wikidex, needs ffmpeg in the data step
  ['eevee;f', false], // no female animation: the still, with an idle class
  ['joltik', false],
  ['wailord', false]
]
const encounter = ([formCode, shiny]: [string, boolean], i: number): object => ({
  pid: 1000 + i, formCode, name: formCode.replace(';f', ''), isShiny: shiny, gender: formCode.endsWith(';f') ? 2 : 1, level: 5 + i, nature: i,
  shinyValue: shiny ? 3 : 4000 + i, ivHp: 31, ivAtk: 12, ivDef: 20, ivSpAtk: 7, ivSpDef: 31, ivSpe: 15
})
// In MONS order; Recent lists them newest first.
const FILES = [
  '/Animated/Default/pikachu.gif',
  '/Animated/Female_Default/pikachu.gif',
  '/Animated/Female_Default/unfezant.gif',
  '/Animated/Shiny/totodile.gif',
  '/Animated/Default/sprigatito.gif',
  '/Animated/Default/ogerpon.gif',
  '/Female_Default/eevee.png',
  '/Animated/Default/joltik.gif',
  '/Animated/Default/wailord.gif'
]

test('dashboard alive: sprite files, themes per window, night switch, back after restarts', async () => {
  fs.mkdirSync(runDir, { recursive: true })
  let backend: ChildProcess | null = null
  let app: ElectronApplication | null = null
  let phases: [string, string] | null = null
  try {
    expect(await answers(), 'something already answers on localhost:5000; stop it first').toBe(false)
    const build = spawnSync('npm run build', { cwd: dashboardDir, shell: true, encoding: 'utf8' })
    fs.writeFileSync(path.join(runDir, 'dashboard-build.log'), `${build.stdout}${build.stderr}`)
    expect(build.status, 'npm run build in PokemonDashboard failed; see dashboard-build.log').toBe(0)
    step('dashboard built')

    backend = await startBackend()
    const metadata = await (await fetch(`${API}/metadata`)).json()
    const names: string[] = (metadata.games.HGSS?.phases ?? []).map((p: { name: string }) => p.name).sort()
    expect(names.length, 'needs two HGSS milestones in Mongo').toBeGreaterThanOrEqual(2)
    // Two milestones, not one twice: AppContext keys a subscription by game and phase only.
    phases = [names[0], names[1]]
    const [PHASE_A, PHASE_B] = phases
    step(`milestone A "${PHASE_A}", milestone B "${PHASE_B}" with game file ${DOTTED}`)

    app = await launch()
    const admin = await windowByTitle(app, 'Pokemon Dashboard — Admin', 60_000)
    const recent = await windowByTitle(app, 'Pokemon Dashboard — Recent Encounters', 60_000)
    await admin.getByRole('button', { name: 'Dashboard Config', exact: true }).click()
    // Shown only once a pushed config with appearance has arrived.
    await expect(admin.locator('[data-window="default"]')).toBeVisible({ timeout: 30_000 })

    // Reset to a known saved config, through the endpoints: the dashboard has no reset control.
    const snapshot = await admin.evaluate(() => (window as any).api.getSignalRState())
    for (const s of snapshot.dashboardConfig.gamePhases) await post('/unsubcribeGamePhase', { game: s.game, phase: s.phase, gameName: s.gameName ?? null })
    await post('/admin/appearance', DEFAULT_APPEARANCE)
    await post('/updateRecentEncounterConfig', 10)
    await expect(admin.getByText('Current: 10', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect.poll(() => app!.windows().filter((p) => p.url().includes('card=phase')).length, { timeout: 30_000 }).toBe(0)
    await expectTheme(admin, 'midnight')
    await expectTheme(recent, 'midnight')
    step(`reset: ${snapshot.dashboardConfig.gamePhases.length} subscription(s) removed, Midnight, no overrides, count 10`)

    // Milestone A through the UI, with no game file.
    const add = admin.locator('h3', { hasText: 'Add Subscription' }).locator('xpath=following-sibling::div[1]')
    await add.locator('select').nth(0).selectOption('HGSS')
    await add.locator('select').nth(1).selectOption(PHASE_A)
    await add.getByRole('button', { name: 'Add', exact: true }).click()
    const milestoneA = await windowByTitle(app, `HGSS — ${PHASE_A}`, 30_000)
    // Milestone B through the endpoint: the dropdown lists only ROM handles, and none here holds a dot.
    await post('/subcribeGamePhase', { game: 'HGSS', phase: PHASE_B, gameName: DOTTED })
    const milestoneB = await windowByTitle(app, `HGSS — ${PHASE_B} (${DOTTED})`, 30_000)
    // Both windows' phase-info loads also refill Recent from the database; let them finish first.
    await sleep(5000)
    step('milestone windows A and B open')

    // Main opens a Pickup window only from a hunt's tally, so this one is opened by hand. It has no Appearance row.
    await app.evaluate(({ BrowserWindow }, query) => {
      const file = decodeURIComponent(new URL(BrowserWindow.getAllWindows()[0].webContents.getURL()).pathname).replace(/^\//, '')
      const win = new BrowserWindow({ width: 480, height: 400, frame: false, webPreferences: { preload: file.replace('renderer/index.html', 'preload/index.js') } })
      win.loadFile(file, { query })
    }, { card: 'pickup', game: DOTTED })
    const pickup = await windowByTitle(app, `Pokemon Dashboard — Pickup: ${DOTTED}`, 30_000)
    step('Pickup window opened by hand')

    // Sample encounters with no game or phase, so no milestone's counts are touched.
    const send = (channel: string, payload: object): Promise<void> => app!.evaluate(({ BrowserWindow }, a) =>
      BrowserWindow.getAllWindows().forEach((w) => w.webContents.send(a.channel, a.payload)), { channel, payload })
    for (let n = 1; n <= MONS.length; n++) {
      await send('signalr-encounters', {
        response: {
          allPokemonCounts: { encounters: 0, shinyEncounters: 0, shiniesCaught: 0 },
          pokemonEncounters: {}, phaseEncounters: {}, caughtPokemons: [],
          latestEncounters: MONS.slice(0, n).map(encounter).reverse(), gameSpecificResponse: {}
        },
        game: '',
        phase: ''
      })
      await sleep(1000)
    }

    // FR-4 and FR-7: the file each sprite loaded, read from the page.
    type Img = { ok: boolean; file: string; cls: string }
    const imgs = (): Promise<Img[]> => recent.locator('.sprite-stage img').evaluateAll((els) => (els as HTMLImageElement[]).map((img) => ({
      ok: img.complete && img.naturalWidth > 0,
      file: decodeURIComponent(new URL(img.currentSrc).pathname),
      cls: img.className
    })))
    const expected = [...FILES].reverse()
    await expect.poll(async () => (await imgs()).map((i) => i.file), {
      timeout: 15_000,
      message: 'sprite files in Recent (a missing ogerpon.gif means the data step ran without ffmpeg)'
    }).toEqual(expected)
    await expect.poll(async () => (await imgs()).every((i) => i.ok), { timeout: 15_000, message: 'every sprite loaded' }).toBe(true)
    const loaded = await imgs()
    step(`sprites: ${loaded.map((i) => `${i.file} [${i.cls}]`).join(', ')}`)
    for (const img of loaded) {
      if (img.file.startsWith('/Animated/')) expect(img.cls, `${img.file} is animated and has no idle class`).toBe('pokemon-sprite')
      else expect(img.cls, `${img.file} is a still and has an idle class`).toMatch(/^pokemon-sprite sprite-(hop|squash|sway|bob)$/)
    }

    // FR-5: drawn at its own size, only shrunk to fit the 96 px box. offsetHeight ignores the entry and idle transforms.
    const size = (file: string): Promise<{ h: number; nw: number; nh: number }> => recent.locator('.sprite-stage img').evaluateAll((els, f) => {
      const img = (els as HTMLImageElement[]).find((i) => decodeURIComponent(new URL(i.currentSrc).pathname) === f)!
      return { h: img.offsetHeight, nw: img.naturalWidth, nh: img.naturalHeight }
    }, file)
    const joltik = await size('/Animated/Default/joltik.gif')
    const wailord = await size('/Animated/Default/wailord.gif')
    step(`sizes: joltik ${JSON.stringify(joltik)}, wailord ${JSON.stringify(wailord)}`)
    expect(joltik.h, 'Joltik is drawn smaller than Wailord').toBeLessThan(wailord.h)
    for (const [name, s] of [['joltik', joltik], ['wailord', wailord]] as const) {
      const fit = s.nh * Math.min(1, 96 / s.nw, 96 / s.nh)
      expect(Math.abs(s.h - fit), `${name} drawn ${s.h} px high, its own size fitted is ${fit}`).toBeLessThanOrEqual(1)
    }
    await recent.screenshot({ path: path.join(runDir, '01-sprites-recent.png') })
    await milestoneA.screenshot({ path: path.join(runDir, '01-sprites-milestone.png') })
    step('screenshots 01-sprites')
  } finally {
    if (backend && (await answers())) {
      try {
        await post('/admin/appearance', DEFAULT_APPEARANCE)
        if (phases) {
          await post('/unsubcribeGamePhase', { game: 'HGSS', phase: phases[0], gameName: null })
          await post('/unsubcribeGamePhase', { game: 'HGSS', phase: phases[1], gameName: DOTTED })
        }
        step('restore: Midnight, default times, A and B unsubscribed')
      } catch (err) {
        step(`restore failed: ${err}`)
      }
    }
    if (app) {
      await app.close()
      step('dashboard closed')
    }
    if (backend) await stopBackend(backend)
  }
})
