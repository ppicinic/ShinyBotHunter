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
  killTree(proc.pid!)
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

const ADMIN_TITLE = 'Pokemon Dashboard — Admin'
const RECENT_TITLE = 'Pokemon Dashboard — Recent Encounters'
const pad = (n: number): string => String(n).padStart(2, '0')
const hhmm = (ms: number): string => {
  const d = new Date(ms)
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`
}

async function shots(name: string, pages: Record<string, Page>): Promise<void> {
  for (const [window, page] of Object.entries(pages)) await page.screenshot({ path: path.join(runDir, `${name}-${window}.png`) })
  step(`screenshots ${name}`)
}

const DEFAULT_APPEARANCE = {
  dayStart: '07:00', nightStart: '19:00', default: { theme: 'midnight' }, windows: {},
  animations: { gradients: 'high', snow: 'high', stars: 'high' }
}
// One level per kind that is not the default, kept through the night wait and both restarts.
const CHOSEN = { gradients: 'low', snow: 'off', stars: 'medium' } as const
const LEVEL_LABELS = { gradients: 'Gradients', snow: 'Snow', stars: 'Stars' } as const
const ATTRIBUTES = { gradients: 'animGradients', snow: 'animSnow', stars: 'animStars' } as const

// The names of the running CSS animations, the pseudo-elements' included.
const running = (page: Page): Promise<string[]> => page.evaluate(() =>
  [...new Set(document.getAnimations().filter((a) => a.playState === 'running').map((a) => (a as CSSAnimation).animationName))].sort())
// A theme's own animation (gradients, glows, lights, snow) or the shiny stars.
const themed = (names: string[]): string[] => names.filter((n) => n.startsWith('theme-') || n === 'sprite-twinkle')
const levels = (page: Page): Promise<Record<string, string | null>> => page.evaluate((attrs) => Object.fromEntries(Object.entries(attrs).map(([kind, attr]) =>
  [kind, `${document.documentElement.dataset[attr]} ${localStorage.getItem(`dashboard-anim-${kind}`)}`])), ATTRIBUTES)
// Attribute and remembered value, which must agree.
const levelsAre = (chosen: Record<string, string>): Record<string, string> => Object.fromEntries(Object.entries(chosen).map(([k, v]) => [k, `${v} ${v}`]))
const snowContent = (page: Page): Promise<string[]> => page.evaluate(() =>
  [getComputedStyle(document.body, '::before'), getComputedStyle(document.body, '::after'), getComputedStyle(document.getElementById('root')!, '::after')].map((l) => l.content))
const rootProps = (page: Page, prefix: string): Promise<string> => page.evaluate((p) =>
  Array.from(document.documentElement.style).filter((n) => n.startsWith(p)).map((n) => `${n}=${document.documentElement.style.getPropertyValue(n)}`).join(' '), prefix)

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

test('dashboard alive: sprite files, themes per window, animation levels, night switch, back after restarts', async () => {
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
    // Reassigned after each restart.
    let admin = await windowByTitle(app, ADMIN_TITLE, 60_000)
    let recent = await windowByTitle(app, RECENT_TITLE, 60_000)
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
    let milestoneA = await windowByTitle(app, `HGSS — ${PHASE_A}`, 30_000)
    // Milestone B through the endpoint: the dropdown lists only ROM handles, and none here holds a dot.
    await post('/subcribeGamePhase', { game: 'HGSS', phase: PHASE_B, gameName: DOTTED })
    let milestoneB = await windowByTitle(app, `HGSS — ${PHASE_B} (${DOTTED})`, 30_000)
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

    // --- themes: every change through the Appearance section --------------------------------------
    const A = phaseName('HGSS', PHASE_A, null)
    const B = phaseName('HGSS', PHASE_B, DOTTED)
    const PICKUP = `pickup:${enc(DOTTED)}`
    const row = (name: string) => admin.locator(`[data-window="${name}"]`)
    const select = (name: string, aria: 'Theme' | 'Night theme') => row(name).locator(`select[aria-label="${aria}"]`)
    const nightBox = (name: string) => row(name).locator('input[aria-label="Switch at night"]')
    // A POST that failed shows "Server returned N" in the tab's feedback box.
    const noServerError = (): Promise<void> => expect(admin.getByText(/Server returned/)).toHaveCount(0)
    const pick = async (name: string, aria: 'Theme' | 'Night theme', option: string | { label: string }, value: string): Promise<void> => {
      await select(name, aria).selectOption(option)
      await expect(select(name, aria)).toHaveValue(value, { timeout: 30_000 })
      await noServerError()
      step(`${name}: ${aria} ${value || 'Default'}`)
    }
    // Checked after the target changed plus 2 s, so a late push would have landed.
    const unchanged = async (expected: [Page, string][]): Promise<void> => {
      await sleep(2000)
      for (const [page, id] of expected) expect(await theme(page), `${page.url()} keeps ${id}`).toBe(id)
    }

    // Default.
    await pick('default', 'Theme', 'hgss-drift', 'hgss-drift')
    for (const [name, page] of [['admin', admin], ['recent', recent], [A, milestoneA], [B, milestoneB], [PICKUP, pickup]] as const) {
      await expectTheme(page, 'hgss-drift')
      expect(await page.evaluate((k) => localStorage.getItem(k), `dashboard-theme:${name}`), `remembered theme of ${name}`).toBe('hgss-drift')
    }

    // An override on A, then Default puts it back.
    await pick(A, 'Theme', 'ruby', 'ruby')
    await expectTheme(milestoneA, 'ruby')
    await unchanged([[admin, 'hgss-drift'], [recent, 'hgss-drift'], [milestoneB, 'hgss-drift'], [pickup, 'hgss-drift']])
    await pick(A, 'Theme', { label: 'Default' }, '')
    await expectTheme(milestoneA, 'hgss-drift')

    // An override on B, the dotted window key, kept for the restarts.
    await pick(B, 'Theme', 'sapphire', 'sapphire')
    await expectTheme(milestoneB, 'sapphire')
    await unchanged([[milestoneA, 'hgss-drift'], [recent, 'hgss-drift'], [pickup, 'hgss-drift']])

    // The Admin row.
    await pick('admin', 'Theme', 'christmas', 'christmas')
    await expectTheme(admin, 'christmas')
    await unchanged([[recent, 'hgss-drift'], [milestoneA, 'hgss-drift'], [pickup, 'hgss-drift'], [milestoneB, 'sapphire']])
    await shots('02-overrides', { admin, recent, milestoneA, milestoneB, pickup })

    // --- the Animations settings: gradients low, snow off, stars medium, through the Admin selectors ---------
    const level = (kind: keyof typeof LEVEL_LABELS) => admin.locator(`select[aria-label="${LEVEL_LABELS[kind]}"]`)
    const five = [['admin', admin], ['recent', recent], ['A', milestoneA], ['B', milestoneB], ['pickup', pickup]] as const
    for (const kind of Object.keys(CHOSEN) as (keyof typeof CHOSEN)[]) await expect(level(kind)).toHaveValue('high')
    // Admin is on Christmas Pine, B on Sapphire, the rest on HGSS Gold and Silver: every one has a theme animation.
    for (const [name, page] of five) {
      await expect.poll(async () => (await running(page)).some((n) => n.startsWith('theme-')), { timeout: 15_000, message: `${name} runs a theme animation at high` }).toBe(true)
    }
    expect(await running(recent), 'the shiny Totodile\'s stars twinkle at high').toContain('sprite-twinkle')
    for (const kind of Object.keys(CHOSEN) as (keyof typeof CHOSEN)[]) {
      await level(kind).selectOption(CHOSEN[kind])
      await expect(level(kind)).toHaveValue(CHOSEN[kind], { timeout: 30_000 })
      await noServerError()
      step(`Animations ${LEVEL_LABELS[kind]}: ${CHOSEN[kind]}`)
    }
    for (const [name, page] of five) {
      await expect.poll(() => levels(page), { timeout: 30_000, message: `${name}: the root attributes and remembered levels` }).toEqual(levelsAre(CHOSEN))
      await expect.poll(async () => themed(await running(page)), { timeout: 15_000, message: `${name}: no theme animation or stars running as CSS` }).toEqual([])
    }
    expect(await snowContent(admin), 'Admin\'s snow with snow off').toEqual(['none', 'none', 'none'])
    // Low and medium: the timers move the root properties instead.
    const moves = async (page: Page, prefix: string): Promise<boolean> => {
      const a = await rootProps(page, prefix)
      await sleep(1500)
      return a !== '' && a !== (await rootProps(page, prefix))
    }
    await expect.poll(() => moves(recent, '--header-pos'), { timeout: 15_000, message: 'Recent\'s header gradient moves at low' }).toBe(true)
    await expect.poll(() => moves(recent, '--twinkle-'), { timeout: 15_000, message: 'Recent\'s stars twinkle at medium' }).toBe(true)
    // The GIFs, the still's idle movement and the drop-in are not theme animations and stay.
    const after = await imgs()
    expect(after.map((i) => i.file), 'the sprite files with the animation levels changed').toEqual(expected)
    expect(after.map((i) => i.cls), 'the sprite classes with the animation levels changed').toEqual(loaded.map((i) => i.cls))
    const idle = after.find((i) => !i.file.startsWith('/Animated/'))!.cls.split(' ')[1]
    expect(await running(recent), `the still's idle movement ${idle}`).toContain(idle)
    expect(await recent.locator('.sprite-stage').evaluateAll((els) => [...new Set(els.map((e) => getComputedStyle(e).animationName))]), 'the drop-in').toEqual(['sprite-enter'])
    await shots('02-animations', { admin, recent, milestoneA, milestoneB, pickup })

    // The night switch on Recent. The box is disabled while the row is on Default, so the theme comes first.
    await pick('recent', 'Theme', 'christmas', 'christmas')
    await nightBox('recent').click()
    await expect(nightBox('recent')).toBeChecked({ timeout: 30_000 })
    const now = Date.now()
    const boundary = Math.ceil(now / 60_000) * 60_000 + 60_000 // 60 to 120 s ahead, on a minute
    for (const [label, value] of [['Day starts', hhmm(now - 60_000)], ['Night starts', hhmm(boundary)]]) {
      const input = admin.locator(`input[aria-label="${label}"]`)
      await input.fill(value)
      await expect(input).toHaveValue(value, { timeout: 30_000 })
      await noServerError()
      step(`${label} ${value}`)
    }
    await pick('recent', 'Night theme', 'christmas-night', 'christmas-night')
    await expectTheme(recent, 'christmas')
    expect(Date.now(), 'the boundary passed during setup; rerun').toBeLessThan(boundary - 5_000)
    await recent.evaluate(() => { (window as any).__qaAlive = 1 })
    let navigations = 0
    recent.on('framenavigated', () => navigations++)
    const others = (): Promise<string[]> => Promise.all([admin, milestoneA, milestoneB, pickup].map(theme))
    const before = await others()
    const appearance = (): Promise<unknown> => admin.evaluate(() => (window as any).api.getSignalRState().then((s: any) => s.dashboardConfig.appearance))
    const set = await appearance()
    // Keys typed on this PC during the wait would land in Admin's last focused select and change the appearance:
    // blur it and give the focus to Recent, which has no inputs.
    await admin.evaluate(() => (document.activeElement as HTMLElement | null)?.blur())
    await app.evaluate(({ BrowserWindow }, title) => BrowserWindow.getAllWindows().find((w) => w.getTitle() === title)?.focus(), RECENT_TITLE)
    step(`waiting for the night boundary at ${new Date(boundary).toISOString()}; appearance ${JSON.stringify(set)}`)
    await expect.poll(() => theme(recent), { timeout: boundary - Date.now() + 20_000, intervals: [250] }).toBe('christmas-night')
    const switchedAt = Date.now()
    step(`Recent switched to christmas-night ${switchedAt - boundary} ms after the boundary`)
    expect(switchedAt, 'Recent switched before the boundary').toBeGreaterThanOrEqual(boundary - 1_000)
    expect(await recent.evaluate(() => (window as any).__qaAlive), 'Recent reloaded at the switch').toBe(1)
    expect(navigations, 'Recent navigated at the switch').toBe(0)
    // The scenario changes nothing during the wait; a change here came from input into the windows (keep hands off them).
    expect(await appearance(), 'the appearance changed during the wait, not by the scenario').toEqual(set)
    expect(await others(), 'Admin, A, B and Pickup at the switch').toEqual(before)
    await shots('03-night', { recent })

    // --- back after a restart of the dashboard, then of the backend --------------------------------
    const expectSaved = async (name: string): Promise<void> => {
      admin = await windowByTitle(app!, ADMIN_TITLE, 60_000)
      recent = await windowByTitle(app!, RECENT_TITLE, 60_000)
      milestoneA = await windowByTitle(app!, `HGSS — ${PHASE_A}`, 60_000)
      milestoneB = await windowByTitle(app!, `HGSS — ${PHASE_B} (${DOTTED})`, 60_000)
      await expect.poll(async () => {
        const state = await admin.evaluate(() => (window as any).api.getSignalRState())
        return state.dashboardConfig.gamePhases
          .map((p: { game: string; phase: string; gameName?: string | null }) => [p.game, p.phase, p.gameName ?? null])
          .sort()
      }, { timeout: 30_000, message: 'subscriptions' }).toEqual([['HGSS', PHASE_A, null], ['HGSS', PHASE_B, DOTTED]].sort())
      await admin.getByRole('button', { name: 'Dashboard Config', exact: true }).click()
      await expect(row('default')).toBeVisible({ timeout: 30_000 })
      await expect(select('default', 'Theme')).toHaveValue('hgss-drift', { timeout: 30_000 })
      await expect(select('admin', 'Theme')).toHaveValue('christmas')
      await expect(select('recent', 'Theme')).toHaveValue('christmas')
      await expect(nightBox('recent')).toBeChecked()
      await expect(select('recent', 'Night theme')).toHaveValue('christmas-night')
      await expect(select(B, 'Theme')).toHaveValue('sapphire')
      await expect(select(A, 'Theme')).toHaveValue('')
      await expectTheme(admin, 'christmas')
      await expectTheme(recent, 'christmas-night')
      await expectTheme(milestoneA, 'hgss-drift')
      await expectTheme(milestoneB, 'sapphire')
      for (const kind of Object.keys(CHOSEN) as (keyof typeof CHOSEN)[]) await expect(level(kind)).toHaveValue(CHOSEN[kind])
      for (const [label, page] of [['admin', admin], ['recent', recent], ['A', milestoneA], ['B', milestoneB]] as const) {
        await expect.poll(() => levels(page), { timeout: 30_000, message: `${label}: the root attributes and remembered levels` }).toEqual(levelsAre(CHOSEN))
        await expect.poll(async () => themed(await running(page)), { timeout: 15_000, message: `${label}: no theme animation running as CSS` }).toEqual([])
      }
      expect(await snowContent(admin), 'Admin\'s snow with snow off').toEqual(['none', 'none', 'none'])
      expect(await snowContent(recent), 'Recent\'s snow with snow off').toEqual(['none', 'none', 'none'])
      await shots(name, { admin, recent, milestoneA, milestoneB })
    }

    await app.close()
    app = await launch()
    step('dashboard restarted')
    await expectSaved('04-after-dashboard-restart')

    // The dashboard is closed while the backend restarts, so what it shows next comes from the saved document.
    await app.close()
    app = null
    const stopped = await stopBackend(backend)
    backend = null
    expect(stopped, 'the backend exited on "end" within 60 s').toBe(true)
    backend = await startBackend()
    app = await launch()
    step('backend and dashboard restarted')
    await expectSaved('05-after-backend-restart')

    // All three back to high for the per-theme screenshots: the theme animations run again.
    for (const kind of Object.keys(CHOSEN) as (keyof typeof CHOSEN)[]) {
      await level(kind).selectOption('high')
      await expect(level(kind)).toHaveValue('high', { timeout: 30_000 })
      await noServerError()
    }
    for (const [label, page] of [['admin', admin], ['recent', recent], ['A', milestoneA], ['B', milestoneB]] as const) {
      await expect.poll(async () => (await running(page)).some((n) => n.startsWith('theme-')), { timeout: 15_000, message: `${label} runs a theme animation at high` }).toBe(true)
    }
    expect(await running(recent), 'Recent\'s snow falls at high').toContain('theme-snow')
    step('Animations all high again')

    // --- one screenshot per theme, ending on Midnight with no overrides ----------------------------
    for (const name of ['admin', 'recent', B]) await pick(name, 'Theme', { label: 'Default' }, '')
    for (const id of THEMES) {
      await pick('default', 'Theme', id, id)
      for (const page of [recent, admin, milestoneA]) await expectTheme(page, id)
      await sleep(2000) // the drift and the snow show
      await shots(id, { recent, admin, milestone: milestoneA })
    }
  } finally {
    if (backend && (await answers())) {
      try {
        await post('/admin/appearance', DEFAULT_APPEARANCE)
        if (phases) {
          await post('/unsubcribeGamePhase', { game: 'HGSS', phase: phases[0], gameName: null })
          await post('/unsubcribeGamePhase', { game: 'HGSS', phase: phases[1], gameName: DOTTED })
        }
        step('restore: Midnight, default times, animations high, A and B unsubscribed')
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
