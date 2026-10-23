import { useProjectStore } from '../store/projectStore'

export function ProjectManager(): React.JSX.Element {
  const project = useProjectStore((state) => state.project)
  const selectWorksheet = useProjectStore((state) => state.selectWorksheet)
  const addWorksheet = useProjectStore((state) => state.addWorksheet)
  const selectGraph = useProjectStore((state) => state.selectGraph)
  const selectedGraphId = useProjectStore((state) => state.selectedGraphId)

  return (
    <aside className="project-manager" aria-label="Gestor de proyecto">
      <header className="panel-title">Gestor de proyecto</header>
      <div className="tree">
        <div className="tree-root">📁 {project.name}</div>
        <div className="tree-section">
          <div className="tree-label">Hojas de trabajo ({project.worksheets.length})</div>
          {project.worksheets.map((worksheet) => (
            <button
              key={worksheet.id}
              type="button"
              className={`tree-item ${worksheet.id === project.activeWorksheetId ? 'active' : ''}`}
              onClick={() => selectWorksheet(worksheet.id)}
            >
              ▦ {worksheet.name} <span className="muted">({worksheet.rowCount} filas)</span>
            </button>
          ))}
          <button type="button" className="tree-action" onClick={addWorksheet}>
            + Nueva hoja
          </button>
        </div>
        <div className="tree-section">
          <div className="tree-label">Salida ({project.session.length})</div>
          <div className="tree-item muted">Últimos {Math.min(3, project.session.length)} bloques en sesión</div>
        </div>
        <div className="tree-section">
          <div className="tree-label">Gráficos ({project.graphs.length})</div>
          {project.graphs.slice(-8).reverse().map((graph) => (
            <button
              key={graph.id}
              type="button"
              className={`tree-item ${graph.id === selectedGraphId ? 'active' : ''}`}
              onClick={() => selectGraph(graph.id)}
            >
              📈 {graph.title}
            </button>
          ))}
        </div>
      </div>
    </aside>
  )
}
