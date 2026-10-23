import { createProject, type ProjectDoc, type SessionBlock } from '../../domain/model/project'
import { createColumn, createWorksheet, type Worksheet } from '../../domain/model/worksheet'
import { err, ok, type Result } from '../../domain/model/result'
import { parseCell, type DataType } from '../../domain/model/value'

/** Serializa el proyecto a un documento JSON estable (formato `.ttab`). */
export function serializeProject(project: ProjectDoc): string {
  return JSON.stringify(project, null, 2)
}

export function deserializeProject(text: string): Result<ProjectDoc> {
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return err('INVALID_INPUT', 'El archivo no es un proyecto TinyTab válido (JSON inválido).')
  }
  if (!parsed || typeof parsed !== 'object') return err('INVALID_INPUT', 'Proyecto vacío o inválido.')
  const candidate = parsed as Partial<ProjectDoc>
  if (candidate.format !== 'tinytab-project' || candidate.version !== 1) {
    return err('INVALID_INPUT', 'Formato de proyecto no reconocido.')
  }
  if (!Array.isArray(candidate.worksheets) || candidate.worksheets.length === 0) {
    return err('INVALID_INPUT', 'El proyecto no contiene hojas de trabajo.')
  }
  return ok({
    format: 'tinytab-project',
    version: 1,
    name: typeof candidate.name === 'string' ? candidate.name : 'Proyecto sin título',
    worksheets: candidate.worksheets as Worksheet[],
    session: Array.isArray(candidate.session) ? (candidate.session as SessionBlock[]) : [],
    graphs: Array.isArray(candidate.graphs) ? candidate.graphs : [],
    activeWorksheetId: typeof candidate.activeWorksheetId === 'string' ? candidate.activeWorksheetId : (candidate.worksheets[0]!.id)
  })
}

function detectDelimiter(text: string): string {
  const firstLine = text.split(/\r?\n/)[0] ?? ''
  const candidates = [',', ';', '\t']
  let best = ','
  let bestCount = 0
  for (const candidate of candidates) {
    const count = firstLine.split(candidate).length - 1
    if (count > bestCount) {
      best = candidate
      bestCount = count
    }
  }
  return best
}

function inferType(values: readonly string[]): DataType {
  const nonEmpty = values.filter((value) => value.trim() !== '')
  if (nonEmpty.length === 0) return 'numeric'
  const numeric = nonEmpty.every((value) => Number.isFinite(Number(value.replace(',', '.'))))
  return numeric ? 'numeric' : 'text'
}

/** Convierte texto CSV en una hoja de trabajo (detecta delimitador y tipos). */
export function importCsv(text: string, name = 'Hoja de trabajo 1', options: { headers?: boolean; delimiter?: string } = {}): Result<Worksheet> {
  const lines = text.split(/\r?\n/).filter((line) => line.trim().length > 0)
  if (lines.length === 0) return err('INVALID_INPUT', 'El archivo CSV está vacío.')
  const delimiter = options.delimiter ?? detectDelimiter(text)
  const rows = lines.map((line) => line.split(delimiter).map((value) => value.trim()))
  const headers = options.headers === false ? undefined : rows.shift()
  const columnCount = Math.max(...rows.map((row) => row.length), 0)
  if (columnCount === 0) return err('INVALID_INPUT', 'El archivo CSV no contiene columnas.')
  const columns = Array.from({ length: columnCount }, (_, index) => {
    const values = rows.map((row) => row[index] ?? '')
    const type = inferType(values)
    const column = createColumn(headers?.[index]?.replace(/^"|"$/g, '') || `C${index + 1}`, type, rows.length)
    return { ...column, values: values.map((value) => parseCell(value.replace(/^"|"$/g, ''), type)) }
  })
  return ok({ ...createWorksheet(name, 0, rows.length), columns, rowCount: rows.length })
}

export function exportWorksheetCsv(worksheet: Worksheet): string {
  const header = worksheet.columns.map((column) => column.name).join(',')
  const rows = Array.from({ length: worksheet.rowCount }, (_, rowIndex) =>
    worksheet.columns
      .map((column) => {
        const value = column.values[rowIndex]
        if (value === null || value === undefined) return ''
        const text = String(value)
        return /[",;\t]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
      })
      .join(',')
  )
  return [header, ...rows].join('\n')
}

/** Genera un HTML autocontenido con la sesión del proyecto. */
export function sessionToHtml(project: ProjectDoc): string {
  const escape = (text: string): string =>
    text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  const blocks = project.session
    .map((block) => {
      const className = block.kind === 'command' ? 'command' : block.kind === 'error' ? 'error' : block.kind === 'note' ? 'note' : 'output'
      return `<section class="${className}"><h3>${escape(block.title ?? block.kind)}</h3><pre>${escape(block.text)}</pre></section>`
    })
    .join('\n')
  return `<!doctype html>
<html lang="es"><head><meta charset="utf-8"><title>${escape(project.name)} — Sesión TinyTab</title>
<style>body{font-family:Segoe UI,Tahoma,sans-serif;margin:24px;color:#1e1e1e;background:#fff}
h1{font-size:20px}h3{font-size:13px;margin:0 0 4px;color:#0a6e9e}
pre{font-family:Consolas,monospace;font-size:12px;white-space:pre-wrap;margin:0}
section{border:1px solid #d4d4d4;padding:8px 12px;margin:8px 0;background:#fafafa}
section.command{background:#f0f6fa}section.error{background:#fdf0f0}section.note{background:#fffbf0}
footer{margin-top:16px;color:#666;font-size:11px}</style></head>
<body><h1>${escape(project.name)}</h1>
<p>TinyTab Statistical Software — GPL-3.0-or-later — ${new Date().toISOString().slice(0, 10)}</p>
${blocks}
<footer>Generado por TinyTab Statistical Software. Licencia GNU GPL v3 o posterior.</footer></body></html>`
}

export function newProject(name = 'Proyecto sin título'): ProjectDoc {
  return createProject(name)
}
