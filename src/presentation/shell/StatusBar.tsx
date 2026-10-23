import { useProjectStore } from '../store/projectStore'
import type { DialogState } from '../store/projectStore'

export function StatusBar(): React.JSX.Element {
  const status = useProjectStore((state) => state.status)
  const project = useProjectStore((state) => state.project)
  const filePath = useProjectStore((state) => state.filePath)
  return (
    <footer className="app-statusbar" role="status">
      <span className="status-message">{status}</span>
      <span className="status-right">
        {project.name}
        {filePath ? ` — ${filePath}` : ' — sin guardar'}
      </span>
    </footer>
  )
}

export type { DialogState }
