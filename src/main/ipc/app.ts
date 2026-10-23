import { app, dialog, type IpcMain } from 'electron'
import { IpcChannels, type DialogMessageArgs } from '@shared/ipc-contract'

export function registerAppIpc(ipc: IpcMain): void {
  ipc.handle(IpcChannels.appVersion, () => app.getVersion())

  ipc.handle(IpcChannels.dialogMessage, async (_event, args: DialogMessageArgs) => {
    const options = {
      type: args.kind === 'warning' ? ('warning' as const) : args.kind === 'error' ? ('error' as const) : ('info' as const),
      title: args.title ?? 'TinyTab',
      message: args.text,
      buttons: ['OK']
    }
    if (args.kind === 'error') await dialog.showMessageBox(options)
    else await dialog.showMessageBox(options)
  })

  ipc.handle(IpcChannels.dialogConfirm, async (_event, text: string, title?: string) => {
    const result = await dialog.showMessageBox({
      type: 'question',
      title: title ?? 'TinyTab',
      message: text,
      buttons: ['Cancelar', 'Aceptar'],
      defaultId: 1,
      cancelId: 0,
      noLink: true
    })
    return result.response === 1
  })
}
