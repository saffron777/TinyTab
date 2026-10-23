import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App } from '@presentation/shell/App'
import { useProjectStore } from '@presentation/store/projectStore'
import { activeWorksheet, createProject } from '@domain/model/project'
import type { TinyTabApi } from '@shared/ipc-contract'

const api: TinyTabApi = {
  app: { version: async () => '0.1.0' },
  dialog: { message: async () => undefined, confirm: async () => true },
  files: {
    openProject: async () => null,
    saveProject: async () => true,
    saveProjectAs: async () => null,
    importCsv: async () => null,
    exportCsv: async () => null,
    exportSession: async () => null,
    exportText: async () => null
  },
  onMenuCommand: () => () => undefined
}

beforeEach(() => {
  window.tinytab = api
  useProjectStore.setState({
    project: createProject('Prueba de interfaz'),
    filePath: null,
    status: 'Listo',
    dialog: { kind: 'none' },
    selectedGraphId: null,
    past: [],
    future: []
  })
  const store = useProjectStore.getState()
  const worksheet = activeWorksheet(store.project)
  const values = [10, 12, 11, 14, 13]
  for (let row = 0; row < values.length; row += 1) store.editCell(worksheet.columns[0]!.id, row, String(values[row]))
})

afterEach(() => cleanup())

describe('interfaz principal', () => {
  it('muestra el shell estilo Minitab', () => {
    render(<App />)
    expect(screen.getByLabelText('Barra de menús')).toBeInTheDocument()
    expect(screen.getByLabelText('Hoja de trabajo')).toBeInTheDocument()
    expect(screen.getByLabelText('Ventana de sesión')).toBeInTheDocument()
    expect(screen.getByLabelText('Gestor de proyecto')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Estadística' })).toBeInTheDocument()
  })

  it('ejecuta una descriptiva desde el menú y la registra en la sesión', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.click(screen.getByRole('button', { name: 'Estadística' }))
    await user.click(await screen.findByRole('button', { name: 'Estadísticas descriptivas' }))
    const dialog = await screen.findByRole('dialog', { name: 'Estadísticas descriptivas' })
    expect(dialog).toBeInTheDocument()
    const selector = dialog.querySelector('select') as HTMLSelectElement
    await user.selectOptions(selector, ['C1'])
    await user.click(screen.getByRole('button', { name: 'Ejecutar' }))
    await waitFor(() => expect(useProjectStore.getState().project.session.length).toBeGreaterThan(0))
    expect(screen.getAllByText(/Estadísticas descriptivas/).length).toBeGreaterThan(0)
  })

  it('ejecuta comandos y actualiza la hoja', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.click(screen.getByRole('button', { name: 'Calc' }))
    await user.click(screen.getByRole('button', { name: 'Editor de comandos…' }))
    const editor = screen.getByRole('textbox') as HTMLTextAreaElement
    await user.clear(editor)
    await user.type(editor, 'SET C2 5 6 7')
    await user.click(screen.getByRole('button', { name: 'Ejecutar' }))
    await waitFor(() => expect(useProjectStore.getState().project.session.length).toBeGreaterThan(0))
    const worksheet = activeWorksheet(useProjectStore.getState().project)
    expect(worksheet.columns[1]?.values.slice(0, 3)).toEqual([5, 6, 7])
  })

  it('expone el catálogo completo en el menú Estadística', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.click(screen.getByRole('button', { name: 'Estadística' }))
    expect(await screen.findByText(/Control de calidad/)).toBeInTheDocument()
    expect(screen.getByText(/Series temporales/)).toBeInTheDocument()
    expect(screen.getByText(/Multivariante/)).toBeInTheDocument()
  })
})
