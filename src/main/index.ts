import { app, BrowserWindow, ipcMain, shell } from 'electron'
import { createMainWindow } from './window'
import { registerFileIpc } from './ipc/files'
import { registerAppIpc } from './ipc/app'

const isDev = !app.isPackaged

app.whenReady().then(() => {
  registerAppIpc(ipcMain)
  registerFileIpc(ipcMain)

  createMainWindow()

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createMainWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

app.on('web-contents-created', (_event, contents) => {
  contents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://')) void shell.openExternal(url)
    return { action: 'deny' }
  })
  contents.on('will-navigate', (event, url) => {
    const allowed = isDev ? ['http://localhost', 'http://127.0.0.1'] : ['file://']
    if (!allowed.some((prefix) => url.startsWith(prefix))) event.preventDefault()
  })
})
