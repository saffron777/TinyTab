import { useEffect, useMemo, useState } from 'react'
import { activeWorksheet } from '@domain/model/project'
import { commandHelp } from '@application/commands/execute'
import { findAnalysis } from '@domain/analysis'
import { useProjectStore } from '../store/projectStore'

function columnsOf(project: ReturnType<typeof useProjectStore.getState>['project']): { id: string; name: string; type: string }[] {
  return activeWorksheet(project).columns.map((column) => ({ id: column.id, name: column.name, type: column.type }))
}

export function AnalysisDialog({ analysisId }: { analysisId: string }): React.JSX.Element | null {
  const project = useProjectStore((state) => state.project)
  const setDialog = useProjectStore((state) => state.setDialog)
  const executeAnalysis = useProjectStore((state) => state.executeAnalysis)
  const analysis = findAnalysis(analysisId)
  const columns = useMemo(() => columnsOf(project), [project])

  const [selections, setSelections] = useState<Record<string, string[]>>(() => {
    const initial: Record<string, string[]> = {}
    for (const requirement of analysis?.numericColumns ?? []) initial[`num:${requirement.key}`] = []
    for (const requirement of analysis?.textColumns ?? []) initial[`txt:${requirement.key}`] = []
    return initial
  })
  const [params, setParams] = useState<Record<string, number | string>>(() => {
    const initial: Record<string, number | string> = {}
    for (const param of analysis?.params ?? []) initial[param.key] = param.defaultValue
    return initial
  })
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!analysis) setDialog({ kind: 'none' })
  }, [analysis, setDialog])

  if (!analysis) return null

  const setSelection = (key: string, values: string[]): void => setSelections((current) => ({ ...current, [key]: values }))

  const submit = async (): Promise<void> => {
    for (const requirement of analysis.numericColumns) {
      if (requirement.required && (selections[`num:${requirement.key}`] ?? []).length === 0) {
        setError(`Seleccione una columna para "${requirement.label}".`)
        return
      }
    }
    for (const requirement of analysis.textColumns ?? []) {
      if (requirement.required && (selections[`txt:${requirement.key}`] ?? []).length === 0) {
        setError(`Seleccione una columna para "${requirement.label}".`)
        return
      }
    }
    setError(null)
    const numeric = analysis.numericColumns.flatMap((requirement) => {
      const names = selections[`num:${requirement.key}`] ?? []
      return names.map((columnName) => ({ key: requirement.key, columnName }))
    })
    const text = (analysis.textColumns ?? []).flatMap((requirement) => {
      const names = selections[`txt:${requirement.key}`] ?? []
      return names.map((columnName) => ({ key: requirement.key, columnName }))
    })
    await executeAnalysis(analysis, { numeric, text }, params)
  }

  return (
    <div className="dialog-overlay" role="dialog" aria-modal="true" aria-label={analysis.label}>
      <div className="dialog">
        <header className="dialog-title">
          {analysis.label}
          <button type="button" className="dialog-close" onClick={() => setDialog({ kind: 'none' })} aria-label="Cerrar">
            ×
          </button>
        </header>
        <div className="dialog-body">
          <p className="dialog-description">{analysis.description}</p>
          {analysis.numericColumns.map((requirement) => (
            <label key={requirement.key} className="dialog-field">
              <span>{requirement.label}</span>
              <select
                multiple={requirement.multiple}
                size={requirement.multiple ? 5 : 1}
                value={requirement.multiple ? selections[`num:${requirement.key}`] ?? [] : selections[`num:${requirement.key}`]?.[0] ?? ''}
                onChange={(event) => {
                  const values = Array.from(event.target.selectedOptions).map((option) => option.value)
                  setSelection(`num:${requirement.key}`, requirement.multiple ? values : values.slice(0, 1))
                }}
              >
                <option value="">(seleccionar)</option>
                {columns.map((column) => (
                  <option key={column.id} value={column.name}>
                    {column.name} ({column.type})
                  </option>
                ))}
              </select>
            </label>
          ))}
          {(analysis.textColumns ?? []).map((requirement) => (
            <label key={requirement.key} className="dialog-field">
              <span>{requirement.label}</span>
              <select
                size={1}
                value={selections[`txt:${requirement.key}`]?.[0] ?? ''}
                onChange={(event) => setSelection(`txt:${requirement.key}`, event.target.value ? [event.target.value] : [])}
              >
                <option value="">(seleccionar)</option>
                {columns.map((column) => (
                  <option key={column.id} value={column.name}>
                    {column.name} ({column.type})
                  </option>
                ))}
              </select>
            </label>
          ))}
          {analysis.params.map((param) => (
            <label key={param.key} className="dialog-field">
              <span>{param.label}</span>
              {param.kind === 'select' ? (
                <select value={String(params[param.key] ?? '')} onChange={(event) => setParams((current) => ({ ...current, [param.key]: event.target.value }))}>
                  {(param.options ?? []).map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  type="number"
                  step={param.kind === 'integer' ? 1 : 'any'}
                  value={String(params[param.key] ?? '')}
                  onChange={(event) => setParams((current) => ({ ...current, [param.key]: event.target.value === '' ? '' : Number(event.target.value) }))}
                />
              )}
            </label>
          ))}
          {error && <div className="dialog-error">{error}</div>}
        </div>
        <footer className="dialog-footer">
          <button type="button" onClick={() => setDialog({ kind: 'none' })}>
            Cancelar
          </button>
          <button type="button" className="primary" onClick={() => void submit()}>
            Ejecutar
          </button>
        </footer>
      </div>
    </div>
  )
}

export function CommandDialog(): React.JSX.Element {
  const [text, setText] = useState('NOTE "Bienvenido a TinyTab"\nHELP')
  const setDialog = useProjectStore((state) => state.setDialog)
  const executeCommandText = useProjectStore((state) => state.executeCommandText)
  return (
    <div className="dialog-overlay" role="dialog" aria-modal="true" aria-label="Editor de comandos">
      <div className="dialog dialog-wide">
        <header className="dialog-title">
          Editor de comandos
          <button type="button" className="dialog-close" onClick={() => setDialog({ kind: 'none' })} aria-label="Cerrar">
            ×
          </button>
        </header>
        <div className="dialog-body">
          <p className="dialog-description">Lenguaje de comandos compatible en subconjunto. Una instrucción por línea.</p>
          <textarea className="command-editor" value={text} onChange={(event) => setText(event.target.value)} spellCheck={false} />
          <details className="command-help">
            <summary>Comandos disponibles</summary>
            <pre>{commandHelp()}</pre>
          </details>
        </div>
        <footer className="dialog-footer">
          <button type="button" onClick={() => setDialog({ kind: 'none' })}>
            Cerrar
          </button>
          <button
            type="button"
            className="primary"
            onClick={async () => {
              await executeCommandText(text)
            }}
          >
            Ejecutar
          </button>
        </footer>
      </div>
    </div>
  )
}

export function HelpDialog({ about = false }: { about?: boolean }): React.JSX.Element {
  const setDialog = useProjectStore((state) => state.setDialog)
  return (
    <div className="dialog-overlay" role="dialog" aria-modal="true" aria-label={about ? 'Acerca de TinyTab' : 'Ayuda'}>
      <div className="dialog">
        <header className="dialog-title">
          {about ? 'Acerca de TinyTab' : 'Ayuda de comandos'}
          <button type="button" className="dialog-close" onClick={() => setDialog({ kind: 'none' })} aria-label="Cerrar">
            ×
          </button>
        </header>
        <div className="dialog-body">
          {about ? (
            <>
              <h3>TinyTab Statistical Software</h3>
              <p>Versión 0.1.0 — software estadístico libre para escritorio.</p>
              <p>Licencia GNU General Public License v3.0 o posterior. Uso gratuito, sin registro.</p>
              <p>Interfaz original inspirada en la usabilidad de Minitab; no afiliada a Minitab, LLC.</p>
              <p>Motor de cálculo propio en TypeScript (ver docs/architecture.md).</p>
            </>
          ) : (
            <pre className="command-help-pre">{commandHelp()}</pre>
          )}
        </div>
        <footer className="dialog-footer">
          <button type="button" className="primary" onClick={() => setDialog({ kind: 'none' })}>
            Cerrar
          </button>
        </footer>
      </div>
    </div>
  )
}
