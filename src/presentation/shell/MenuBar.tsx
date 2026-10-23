import { useState } from 'react'
import { analysesByCategory } from '@domain/analysis'
import { useProjectStore } from '../store/projectStore'

interface MenuItem {
  label: string
  action?: () => void
  disabled?: boolean
  separator?: boolean
  children?: MenuItem[]
}

export function MenuBar(): React.JSX.Element {
  const [open, setOpen] = useState<string | null>(null)
  const store = useProjectStore()

  const close = (): void => setOpen(null)

  const menus: { label: string; items: MenuItem[] }[] = [
    {
      label: 'Archivo',
      items: [
        { label: 'Nuevo proyecto', action: store.newProject },
        { label: 'Abrir proyecto…', action: () => void store.openProject() },
        { label: 'Guardar', action: () => void store.saveProject(false) },
        { label: 'Guardar como…', action: () => void store.saveProject(true) },
        { separator: true, label: '' },
        { label: 'Importar CSV…', action: () => void store.importCsvFile() },
        { label: 'Exportar hoja a CSV…', action: () => void store.exportCsvFile() },
        { label: 'Exportar sesión a HTML…', action: () => void store.exportSession() },
        { separator: true, label: '' },
        { label: 'Salir', action: () => window.close() }
      ]
    },
    {
      label: 'Editar',
      items: [
        { label: 'Deshacer', action: store.undo },
        { label: 'Rehacer', action: store.redo }
      ]
    },
    {
      label: 'Datos',
      items: [
        { label: 'Insertar fila', action: store.insertRow },
        { label: 'Insertar columna', action: store.insertColumn },
        { label: 'Nueva hoja de trabajo', action: store.addWorksheet }
      ]
    },
    {
      label: 'Calc',
      items: [
        { label: 'Editor de comandos…', action: () => store.setDialog({ kind: 'command' }) },
        { label: 'Ayuda de comandos', action: store.showHelp }
      ]
    },
    {
      label: 'Estadística',
      items: analysesByCategory().map((group) => ({
        label: group.category,
        children: group.analyses.map((analysis) => ({
          label: analysis.label,
          action: () => store.setDialog({ kind: 'analysis', analysisId: analysis.id })
        }))
      }))
    },
    {
      label: 'Gráfica',
      items: [
        {
          label: 'Histograma de la primera columna',
          action: () => {
            const worksheet = store.project.worksheets.find((item) => item.id === store.project.activeWorksheetId)
            const column = worksheet?.columns[0]
            if (!column) return
            const values = column.values.map((value) => (typeof value === 'number' ? value : Number.NaN)).filter((value) => Number.isFinite(value))
            store.setStatus(`Histograma: ${column.name} (${values.length} valores)`)
          }
        }
      ]
    },
    {
      label: 'Ver',
      items: [
        { label: 'Sesión', action: () => store.setStatus('La sesión está visible en el panel inferior') },
        { label: 'Gestor de proyecto', action: () => store.setStatus('El gestor de proyecto está visible a la izquierda') }
      ]
    },
    {
      label: 'Ayuda',
      items: [
        { label: 'Comandos disponibles', action: store.showHelp },
        { label: 'Acerca de TinyTab', action: () => store.setDialog({ kind: 'about' }) }
      ]
    }
  ]

  return (
    <nav className="app-menubar" aria-label="Barra de menús">
      {menus.map((menu) => (
        <div key={menu.label} className="menu">
          <button
            type="button"
            className={`menu-item ${open === menu.label ? 'open' : ''}`}
            onClick={() => setOpen(open === menu.label ? null : menu.label)}
            onMouseEnter={() => open && setOpen(menu.label)}
          >
            {menu.label}
          </button>
          {open === menu.label && (
            <div className="menu-dropdown" role="menu">
              {menu.items.map((item, index) =>
                item.separator ? (
                  <hr key={`sep-${index}`} />
                ) : item.children ? (
                  <div key={item.label} className="menu-submenu">
                    <span className="menu-entry has-children">{item.label} ▸</span>
                    <div className="menu-submenu-content">
                      {item.children.map((child) => (
                        <button
                          key={child.label}
                          type="button"
                          className="menu-entry"
                          onClick={() => {
                            child.action?.()
                            close()
                          }}
                        >
                          {child.label}
                        </button>
                      ))}
                    </div>
                  </div>
                ) : (
                  <button
                    key={item.label}
                    type="button"
                    className="menu-entry"
                    disabled={item.disabled}
                    onClick={() => {
                      item.action?.()
                      close()
                    }}
                  >
                    {item.label}
                  </button>
                )
              )}
            </div>
          )}
        </div>
      ))}
    </nav>
  )
}
