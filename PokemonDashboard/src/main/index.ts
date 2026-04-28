import { app, shell, BrowserWindow, ipcMain, dialog } from 'electron'
import { join } from 'path'
import { readFileSync } from 'fs'

const isDev = process.env.NODE_ENV === 'development'

function loadConfig(): object {
  const configPath = isDev
    ? join(app.getAppPath(), 'config.json')
    : join(process.resourcesPath, 'config.json')

  const raw = readFileSync(configPath, 'utf-8') // throws if missing
  return JSON.parse(raw) // throws if invalid JSON
}

function createWindow(): void {
  const mainWindow = new BrowserWindow({
    width: 1600,
    height: 900,
    show: false,
    autoHideMenuBar: true,
    title: 'Pokemon Dashboard',
    backgroundColor: '#0d0d1a',
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      webSecurity: false // Allow loading local file:// sprites
    }
  })

  mainWindow.on('ready-to-show', () => {
    mainWindow.show()
  })

  mainWindow.webContents.setWindowOpenHandler((details) => {
    shell.openExternal(details.url)
    return { action: 'deny' }
  })

  if (isDev && process.env['ELECTRON_RENDERER_URL']) {
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])
  } else {
    mainWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

app.whenReady().then(() => {
  app.setAppUserModelId('com.pokemon.dashboard')

  // IPC: Load static config.json — fails the app if config is missing or invalid
  ipcMain.handle('load-config', () => {
    return loadConfig()
  })

  createWindow()

  app.on('activate', function () {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

// Catch unhandled errors and show a dialog before quitting
process.on('uncaughtException', (err) => {
  dialog.showErrorBox('Fatal Error', `${err.message}\n\n${err.stack ?? ''}`)
  app.quit()
})
