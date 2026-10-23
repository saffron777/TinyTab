import { useState } from 'react'
import { activeWorksheet } from '@domain/model/project'
import { formatCell } from '@domain/model/value'
import { useProjectStore } from '../store/projectStore'

export function WorksheetView(): React.JSX.Element {
  const project = useProjectStore((state) => state.project)
  const editCell = useProjectStore((state) => state.editCell)
  const insertRow = useProjectStore((state) => state.insertRow)
  const deleteRow = useProjectStore((state) => state.deleteRow)
  const insertColumn = useProjectStore((state) => state.insertColumn)
  const deleteColumn = useProjectStore((state) => state.deleteColumn)
  const renameColumn = useProjectStore((state) => state.renameColumnName)
  const changeType = useProjectStore((state) => state.changeColumnType)
  const [editing, setEditing] = useState<{ columnId: string; row: number } | null>(null)
  const [draft, setDraft] = useState('')
  const [renaming, setRenaming] = useState<string | null>(null)
  const [draftName, setDraftName] = useState('')

  const worksheet = activeWorksheet(project)
  const rows = Array.from({ length: worksheet.rowCount }, (_, index) => index)

  const commit = (): void => {
    if (!editing) return
    editCell(editing.columnId, editing.row, draft)
    setEditing(null)
    setDraft('')
  }

  return (
    <section className="worksheet" aria-label="Hoja de trabajo">
      <header className="panel-title">
        {worksheet.name}
        <span className="panel-subtitle">
          {worksheet.columns.length} columnas · {worksheet.rowCount} filas
        </span>
      </header>
      <div className="worksheet-scroll">
        <table className="grid">
          <thead>
            <tr>
              <th className="corner"> </th>
              {worksheet.columns.map((column) => (
                <th key={column.id} className="column-header">
                  {renaming === column.id ? (
                    <input
                      autoFocus
                      value={draftName}
                      onChange={(event) => setDraftName(event.target.value)}
                      onBlur={() => {
                        renameColumn(column.id, draftName)
                        setRenaming(null)
                      }}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                          renameColumn(column.id, draftName)
                          setRenaming(null)
                        }
                      }}
                    />
                  ) : (
                    <button
                      type="button"
                      className="column-name"
                      title="Doble clic para renombrar"
                      onDoubleClick={() => {
                        setRenaming(column.id)
                        setDraftName(column.name)
                      }}
                    >
                      {column.name}
                    </button>
                  )}
                  <div className="column-tools">
                    <select
                      aria-label={`Tipo de ${column.name}`}
                      value={column.type}
                      onChange={(event) => changeType(column.id, event.target.value as 'numeric' | 'text' | 'datetime')}
                    >
                      <option value="numeric">Numérico</option>
                      <option value="text">Texto</option>
                      <option value="datetime">Fecha</option>
                    </select>
                    <button type="button" title="Eliminar columna" onClick={() => deleteColumn(column.id)}>
                      ×
                    </button>
                  </div>
                </th>
              ))}
              <th className="add-column">
                <button type="button" onClick={insertColumn} title="Insertar columna">
                  ＋
                </button>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row} className="grid-row">
                <th className="row-header">
                  <span>{row + 1}</span>
                  <button type="button" title="Eliminar fila" onClick={() => deleteRow(row + 1)}>
                    ×
                  </button>
                </th>
                {worksheet.columns.map((column) => {
                  const value = column.values[row] ?? null
                  const isEditing = editing?.columnId === column.id && editing.row === row
                  return (
                    <td
                      key={column.id}
                      className={`grid-cell ${column.type}`}
                      onDoubleClick={() => {
                        setEditing({ columnId: column.id, row })
                        setDraft(value === null ? '' : String(value))
                      }}
                    >
                      {isEditing ? (
                        <input
                          autoFocus
                          value={draft}
                          onChange={(event) => setDraft(event.target.value)}
                          onBlur={commit}
                          onKeyDown={(event) => {
                            if (event.key === 'Enter') commit()
                            if (event.key === 'Escape') setEditing(null)
                          }}
                        />
                      ) : (
                        formatCell(value, column.type)
                      )}
                    </td>
                  )
                })}
                <td className="add-column" />
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <footer className="worksheet-footer">
        <button type="button" onClick={insertRow}>
          ＋ Añadir fila
        </button>
        <span className="muted">Doble clic en una celda para editar · doble clic en C1 para renombrar</span>
      </footer>
    </section>
  )
}
