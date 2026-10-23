import { BrowserWindow } from 'electron'
import { join } from 'node:path'

/**
 * Ventana principal de TinyTab. El renderer vive en src/presentation y se
 * sirve con electron-vite en desarrollo; en producción se carga el bundle.
 */
export function createMainWindow(): BrowserWindow {
  const window = new BrowserWindow({
    width: 1280,
    height: 860,
    minWidth: 960,
    minHeight: 600,
    show: false,
    backgroundColor: '#f0f0f0',
    title: 'TinyTab Statistical Software',
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false
    }
  })

  window.once('ready-to-show', () => window.show())
  window.setMenuBarVisibility(false)

  const rendererUrl = process.env['ELECTRON_RENDERER_URL']
  if (rendererUrl) {
    void window.loadURL(rendererUrl)
  } else {
    void window.loadFile(join(__dirname, '../renderer/index.html'))
  }

  return window
}
