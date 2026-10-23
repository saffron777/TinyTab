import { useProjectStore } from '../store/projectStore'

export function Toolbar(): React.JSX.Element {
  const store = useProjectStore()
  return (
    <div className="app-toolbar" role="toolbar" aria-label="Barra de herramientas">
      <button type="button" title="Nuevo proyecto" onClick={store.newProject}>
        🗋
      </button>
      <button type="button" title="Abrir proyecto" onClick={() => void store.openProject()}>
        📂
      </button>
      <button type="button" title="Guardar" onClick={() => void store.saveProject(false)}>
        💾
      </button>
      <span className="toolbar-separator" />
      <button type="button" title="Deshacer" onClick={store.undo}>
        ↶
      </button>
      <button type="button" title="Rehacer" onClick={store.redo}>
        ↷
      </button>
      <span className="toolbar-separator" />
      <button type="button" title="Insertar fila" onClick={store.insertRow}>
        ＋Fila
      </button>
      <button type="button" title="Insertar columna" onClick={store.insertColumn}>
        ＋Col
      </button>
      <span className="toolbar-separator" />
      <button type="button" title="Editor de comandos" onClick={() => store.setDialog({ kind: 'command' })}>
        ⌨ Comandos
      </button>
      <button type="button" title="Ayuda" onClick={store.showHelp}>
        ？
      </button>
    </div>
  )
}
