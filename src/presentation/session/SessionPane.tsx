import { useEffect, useRef } from 'react'
import { useProjectStore } from '../store/projectStore'

const KIND_LABEL: Record<string, string> = {
  command: 'Comando',
  output: 'Salida',
  error: 'Error',
  note: 'Nota'
}

export function SessionPane(): React.JSX.Element {
  const project = useProjectStore((state) => state.project)
  const endRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' })
  }, [project.session.length])

  return (
    <section className="session-pane" aria-label="Ventana de sesión">
      <header className="panel-title">
        Sesión
        <span className="panel-subtitle">{project.session.length} bloques</span>
      </header>
      <div className="session-content">
        {project.session.length === 0 && (
          <div className="session-empty">
            La sesión está vacía. Ejecute un análisis desde el menú <b>Estadística</b> o escriba comandos en <b>Calc → Editor de comandos</b>.
          </div>
        )}
        {project.session.map((block) => (
          <article key={block.id} className={`session-block ${block.kind}`}>
            <div className="session-block-title">
              {KIND_LABEL[block.kind] ?? block.kind}
              {block.title ? ` — ${block.title}` : ''}
            </div>
            <pre>{block.text}</pre>
          </article>
        ))}
        <div ref={endRef} />
      </div>
    </section>
  )
}
