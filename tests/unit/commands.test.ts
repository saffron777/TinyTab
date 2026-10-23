import { describe, expect, it } from 'vitest'
import { createProject, activeWorksheet } from '@domain/model/project'
import { setCell } from '@domain/model/worksheet'
import { executeCommands } from '@application/commands/execute'
import { offlineAnalysisRunner } from '@application/usecases/analysis-runner'
import { columnByName } from '@domain/model/worksheet'

function projectWithData(): ReturnType<typeof createProject> {
  let project = createProject('Prueba')
  const worksheet = activeWorksheet(project)
  const first = worksheet.columns[0]!
  const second = worksheet.columns[1]!
  for (let index = 0; index < 6; index += 1) {
    project = { ...project, worksheets: project.worksheets.map((item) => (item.id === worksheet.id ? setCell(setCell(item, first.id, index, index + 1), second.id, index, (index + 1) * 2) : item)) }
  }
  return project
}

describe('lenguaje de comandos', () => {
  it('SET, NAME, LET y PRINT actualizan la hoja y la sesión', async () => {
    const project = createProject('Comandos')
    const outcome = await executeCommands(project, 'SET C1 1 2 3 4\nLET C2 = C1 * 2 + 1\nNAME C1 "Datos"\nPRINT C2', offlineAnalysisRunner)
    const worksheet = activeWorksheet(outcome.project)
    expect(columnByName(worksheet, 'Datos')).toBeDefined()
    const doubled = columnByName(worksheet, 'C2')
    expect(doubled?.values.slice(0, 4)).toEqual([3, 5, 7, 9])
    expect(outcome.blocks.some((block) => block.kind === 'error')).toBe(false)
    expect(outcome.project.session.length).toBeGreaterThan(0)
  })

  it('DESCRIBE ejecuta el análisis y agrega salida', async () => {
    const project = projectWithData()
    const outcome = await executeCommands(project, 'DESCRIBE C1', offlineAnalysisRunner)
    expect(outcome.blocks.some((block) => block.kind === 'error')).toBe(false)
    expect(outcome.project.session.some((block) => block.kind === 'output')).toBe(true)
  })

  it('informa comandos desconocidos sin romper la sesión', async () => {
    const outcome = await executeCommands(createProject(), 'INVENTADO 1 2', offlineAnalysisRunner)
    expect(outcome.blocks.some((block) => block.kind === 'error')).toBe(true)
  })
})
