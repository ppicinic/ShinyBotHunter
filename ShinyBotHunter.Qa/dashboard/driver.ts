import { spawn, spawnSync, type ChildProcess } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'
import { _electron, type ElectronApplication, type Page } from '@playwright/test'

export const repoDir = path.resolve(__dirname, '..', '..')
export const dashboardDir = path.join(repoDir, 'PokemonDashboard')

const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15)
export const runDir = path.join(__dirname, 'qa-runs', `${stamp}-hgss-route33-dashboard`)
fs.mkdirSync(runDir, { recursive: true })

export function step(message: string): void {
  const line = `${new Date().toISOString()} ${message}`
  console.log(line)
  fs.appendFileSync(path.join(runDir, 'steps.log'), line + '\n')
}

const sleep = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms))

// --- backend ---------------------------------------------------------------------------------------

export async function startBackend(): Promise<ChildProcess> {
  const log = fs.openSync(path.join(runDir, 'backend.log'), 'w')
  const proc = spawn('dotnet', ['run', '-c', 'Debug'], { cwd: path.join(repoDir, 'ShinyBotHunter'), stdio: ['pipe', log, log] })
  const deadline = Date.now() + 4 * 60 * 1000
  while (Date.now() < deadline) {
    if (proc.exitCode !== null) throw new Error(`backend exited with ${proc.exitCode}; see backend.log`)
    try {
      if ((await fetch('http://localhost:5000/api/getAllGames')).ok) return proc
    } catch {
      // not listening yet
    }
    await sleep(2000)
  }
  throw new Error('backend did not answer on localhost:5000 within 4 minutes')
}

/** Writes "end" to the backend's stdin; resolves with its exit code, or null if it is still running after 60 s. */
export function stopBackend(proc: ChildProcess): Promise<number | null> {
  return new Promise((resolve) => {
    const timer = setTimeout(() => resolve(null), 60_000)
    proc.once('exit', (code) => {
      clearTimeout(timer)
      resolve(code)
    })
    proc.stdin!.write('end\n')
  })
}

// --- dashboard -------------------------------------------------------------------------------------

export function launchDashboard(videoDir: string): Promise<ElectronApplication> {
  const env: Record<string, string> = { ...(process.env as Record<string, string>), NODE_ENV: 'development' }
  delete env.ELECTRON_RENDERER_URL
  // Launched by folder so app.getAppPath() is PokemonDashboard/, where the dev config is read from;
  // Electron then runs out/main/index.js through package.json "main".
  return _electron.launch({
    executablePath: path.join(dashboardDir, 'node_modules', 'electron', 'dist', 'electron.exe'),
    args: [dashboardDir],
    cwd: dashboardDir,
    env,
    recordVideo: { dir: videoDir, size: { width: 820, height: 900 } }
  })
}

export async function windowByTitle(app: ElectronApplication, title: string, timeoutMs: number): Promise<Page> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    for (const page of app.windows()) {
      if ((await page.title()) === title) return page
    }
    await sleep(500)
  }
  throw new Error(`no window titled "${title}" within ${timeoutMs / 1000} s`)
}

/** A screenshot of every dashboard window, plus the emulator while the recorder runs. */
export async function shotAll(name: string, pages: Record<string, Page>): Promise<void> {
  for (const [window, page] of Object.entries(pages)) {
    await page.screenshot({ path: path.join(runDir, `${name}-${window}.png`) })
  }
  await shotEmulator(`${name}-emulator`)
  step(`screenshots ${name}`)
}

// --- emulator over ApiHawk (position 0 listens on 20000 for commands and 20001 for memory) ---------

async function readMemory(addr: number, length: number): Promise<Buffer> {
  const body = Buffer.alloc(9)
  body.writeUInt8(1, 0)
  body.writeUInt32LE(addr, 1)
  body.writeUInt32LE(length, 5)
  const res = await fetch('http://127.0.0.1:20001/', { method: 'POST', body })
  return Buffer.from(await res.arrayBuffer())
}

export async function readU16(addr: number): Promise<number> {
  return (await readMemory(addr, 2)).readUInt16LE(0)
}

export async function readU32(addr: number): Promise<number> {
  return (await readMemory(addr, 4)).readUInt32LE(0)
}

export function toS16(v: number): number {
  return ((Math.floor(v / 65536) + 32768) % 65536) - 32768
}

export async function runMacro(macro: string): Promise<void> {
  await fetch('http://127.0.0.1:20000/command', { method: 'POST', body: 'macro_string_' + macro })
  const deadline = Date.now() + 30_000
  while (Date.now() < deadline) {
    const res = await fetch('http://127.0.0.1:20000/wait-macro', { method: 'POST', body: '' })
    if ((await res.text()).trim() === 'done') return
  }
  throw new Error(`macro ${macro} did not finish within 30 s`)
}

// --- Windows: hotkey, recorder, processes ----------------------------------------------------------

function powershell(script: string): string {
  const encoded = Buffer.from(script, 'utf16le').toString('base64')
  return spawnSync('powershell', ['-NoProfile', '-EncodedCommand', encoded], { encoding: 'utf8' }).stdout.trim()
}

/** Focuses EmuHawk and sends Ctrl+Shift+F12 (Toggle All Cheats in the QA install). Returns whether EmuHawk had the foreground. */
export function sendCheatHotkey(): boolean {
  const out = powershell(`
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class K {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
}
'@
$h = (Get-Process EmuHawk | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).MainWindowHandle
[void][K]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300
$fg = ([K]::GetForegroundWindow() -eq $h)
foreach ($vk in 0x11,0x10,0x7B) { [K]::keybd_event([byte]$vk, [byte][K]::MapVirtualKey($vk,0), 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
Start-Sleep -Milliseconds 120
foreach ($vk in 0x7B,0x10,0x11) { [K]::keybd_event([byte]$vk, [byte][K]::MapVirtualKey($vk,0), 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
"foreground=$fg"`)
  step(`hotkey sent: ${out}`)
  return out.endsWith('foreground=True')
}

// Not locked: one scenario runs per worker process. The test sets this once, and stopRecorder clears it
// (called by the test and again by its finally block), never concurrently.
let recorder: ChildProcess | null = null
const stopFile = path.join(runDir, 'stop-recorder')

export function startEmulatorRecorder(): void {
  recorder = spawn('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(__dirname, 'record-window.ps1'),
    '-ProcessName', 'EmuHawk', '-OutFile', path.join(runDir, 'emulator.mp4'), '-StopFile', stopFile, '-ShotDir', runDir], { stdio: 'ignore' })
  step('emulator recorder started')
}

async function shotEmulator(name: string): Promise<void> {
  if (!recorder) return
  fs.writeFileSync(path.join(runDir, `shot-${name}.request`), '')
  const png = path.join(runDir, `${name}.png`)
  const deadline = Date.now() + 5000
  while (Date.now() < deadline && !fs.existsSync(png)) await sleep(100)
  if (!fs.existsSync(png)) step(`emulator screenshot ${name} not written within 5 s`)
}

export async function stopRecorder(): Promise<void> {
  if (!recorder) return
  const proc = recorder
  recorder = null
  if (proc.exitCode === null) {
    const exited = new Promise((resolve) => proc.once('exit', resolve))
    fs.writeFileSync(stopFile, '')
    await exited
  }
  step(`emulator recorder stopped (exit ${proc.exitCode})`)
}

export function emuHawkRunning(): boolean {
  return spawnSync('tasklist', ['/FI', 'IMAGENAME eq EmuHawk.exe', '/NH'], { encoding: 'utf8' }).stdout.includes('EmuHawk.exe')
}

export function killTree(pid: number): void {
  spawnSync('taskkill', ['/T', '/F', '/PID', String(pid)])
}

export function killEmuHawk(): void {
  spawnSync('taskkill', ['/F', '/IM', 'EmuHawk.exe'])
}

export function dolphinRunning(): boolean {
  return spawnSync('tasklist', ['/FI', 'IMAGENAME eq Dolphin.exe', '/NH'], { encoding: 'utf8' }).stdout.includes('Dolphin.exe')
}

export function killDolphin(): void {
  spawnSync('taskkill', ['/F', '/IM', 'Dolphin.exe'])
}
