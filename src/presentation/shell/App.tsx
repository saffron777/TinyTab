import { useEffect } from 'react'
import { MenuBar } from './MenuBar'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { WorksheetView } from '../worksheet/WorksheetView'
import { SessionPane } from '../session/SessionPane'
import { ProjectManager } from '../project/ProjectManager'
import { ChartView } from '../charts/ChartView'
import { AnalysisDialog, CommandDialog, HelpDialog } from '../dialogs/Dialogs'
import { useProjectStore } from '../store/projectStore'
import '../theme/app.css'

export function App(): React.JSX.Element {
  const dialog = useProjectStore((state) => state.dialog)
  const undo = useProjectStore((state) => state.undo)
  const redo = useProjectStore((state) => state.redo)
  const saveProject = useProjectStore((state) => state.saveProject)
  const project = useProjectStore((state) => state.project)
  const selectedGraphId = useProjectStore((state) => state.selectedGraphId)
  const selectedGraph = selectedGraphId ? project.graphs.find((graph) => graph.id === selectedGraphId) : project.graphs[project.graphs.length - 1]

  useEffect(() => {
    const handler = (event: KeyboardEvent): void => {
      const control = event.ctrlKey || event.metaKey
      if (!control) return
      const key = event.key.toLowerCase()
      if (key === 'z') {
        event.preventDefault()
        undo()
      } else if (key === 'y') {
        event.preventDefault()
        redo()
      } else if (key === 's') {
        event.preventDefault()
        void saveProject()
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [undo, redo, saveProject])

  return (
    <div className="app-shell">
      <MenuBar />
      <Toolbar />
      <div className="app-body">
        <ProjectManager />
        <div className="main-area">
          <div className="worksheet-area">
            <WorksheetView />
          </div>
          <div className="bottom-area">
            <SessionPane />
            <ChartView graph={selectedGraph} />
          </div>
        </div>
      </div>
      <StatusBar />
      {dialog.kind === 'analysis' && <AnalysisDialog analysisId={dialog.analysisId} />}
      {dialog.kind === 'command' && <CommandDialog />}
      {dialog.kind === 'help' && <HelpDialog />}
      {dialog.kind === 'about' && <HelpDialog about />}
    </div>
  )
}
