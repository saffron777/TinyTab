import { describe, expect, it } from 'vitest'
import { activeWorksheet, createProject } from '@domain/model/project'
import { setCell } from '@domain/model/worksheet'
import { deserializeProject, exportWorksheetCsv, importCsv, serializeProject, sessionToHtml } from '@application/usecases/project'
import { appendSession } from '@domain/model/project'

describe('persistencia', () => {
  it('serializa y deserializa un proyecto', () => {
    const project = createProject('Mi proyecto')
    const text = serializeProject(project)
    const restored = deserializeProject(text)
    expect(restored.ok).toBe(true)
    if (restored.ok) expect(restored.value.name).toBe('Mi proyecto')
  })

  it('rechaza documentos inválidos', () => {
    expect(deserializeProject('no es json').ok).toBe(false)
    expect(deserializeProject('{"format":"otro"}').ok).toBe(false)
  })

  it('importa CSV con encabezados e infiere tipos', () => {
    const csv = 'Peso;Altura\n70;170\n80;180\n60;165'
    const worksheet = importCsv(csv, 'Datos')
    expect(worksheet.ok).toBe(true)
    if (!worksheet.ok) return
    expect(worksheet.value.columns.map((column) => column.name)).toEqual(['Peso', 'Altura'])
    expect(worksheet.value.columns[0]?.type).toBe('numeric')
    expect(worksheet.value.columns[0]?.values[0]).toBe(70)
  })

  it('exporta CSV y HTML de sesión', () => {
    let project = createProject('Proyecto')
    const worksheet = activeWorksheet(project)
    project = {
      ...project,
      worksheets: project.worksheets.map((item) => (item.id === worksheet.id ? setCell(item, worksheet.columns[0]!.id, 0, 42) : item))
    }
    project = appendSession(project, { kind: 'output', title: 'Salida', text: 'contenido' })
    const csv = exportWorksheetCsv(activeWorksheet(project))
    expect(csv.split('\n')[1]?.startsWith('42')).toBe(true)
    const html = sessionToHtml(project)
    expect(html).toContain('contenido')
    expect(html).toContain('GNU GPL')
  })
})
