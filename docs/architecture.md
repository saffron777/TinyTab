# Arquitectura de TinyTab

## Principios

1. **Clean Architecture**: regla de dependencia `presentation → application → domain`; `infrastructure` y
   `main` implementan puertos. El dominio no conoce React, Electron ni Node.
2. **Funciones puras y deterministas**: los cálculos estadísticos no dependen de estado global; el RNG
   recibe semilla explícita.
3. **Resultados explícitos**: el dominio devuelve `Result<T, DomainError>`; nunca lanza excepciones para
   errores de usuario.
4. **UI declarativa por registro**: los diálogos de análisis se generan a partir de `AnalysisDefinition`
   (columnas requeridas, parámetros y función `run`).
5**Trazabilidad**: cada salida se registra en la sesión del proyecto y puede exportarse a HTML.

## Capas

### `src/domain`
- `model/`: `Column`, `Worksheet`, `ProjectDoc`, `Cell`, `Result`, `ChartSpec`.
- `stats/`: núcleo numérico (`numeric.ts`), distribuciones (21), descriptiva, tablas, pruebas, ANOVA,
  regresión, SPC/capacidad, MSA, DOE, series temporales, fiabilidad, multivariante, potencia, álgebra lineal.
- `analysis/`: registro de análisis y formateo de resultados para la sesión.

### `src/application`
- `ports/`: `AnalysisRunner`, `ProjectRepository`, `FileGateway`.
- `usecases/`: `run-analysis`, `analysis-runner` (implementación offline), `project` (serialización, CSV, HTML).
- `commands/`: lexer, parser de expresiones, intérprete de comandos Minitab-like.

### `src/infrastructure`
- `electron-file-gateway.ts`: adapta `window.tinytab` (IPC del preload) al puerto `FileGateway`.

### `src/presentation`
- `shell/`: `App`, `MenuBar`, `Toolbar`, `StatusBar`.
- `worksheet/`: rejilla editable con tipos y faltantes.
- `session/`: ventana de sesión.
- `project/`: gestor de proyecto.
- `dialogs/`: diálogo genérico de análisis, editor de comandos y ayuda.
- `charts/`: renderizadores SVG propios.
- `store/`: estado de la aplicación (Zustand) que orquesta casos de uso.

### `src/main` y `src/preload`
- Ventana con `contextIsolation`, `sandbox` y sin `nodeIntegration`.
- IPC validado en `src/shared/ipc-contract.ts`; los handlers sólo leen/escriben archivos elegidos por el
  usuario mediante diálogos nativos.

## Flujo de una ejecución

```
UI (diálogo) → store.executeAnalysis → application.runAnalysis
   → domain.analysis[registry].run(input)
   → resultado → sesión + gráficos del proyecto
   → render en SessionPane / ChartView
```

## Flujo de archivos

```
FileGateway (preload) ⇄ main/ipc/files ⇄ fs/promises
ProjectDoc ⇄ serializeProject/deserializeProject (.ttab)
Worksheet  ⇄ importCsv/exportWorksheetCsv (.ttw/.csv)
Sesión     ⇄ sessionToHtml (.html)
```

## Pruebas

- `tests/unit/numeric.test.ts`: funciones especiales y cuantiles.
- `tests/unit/distributions.test.ts`: 21 distribuciones, parámetros, RNG.
- `tests/unit/statistics.test.ts`: pruebas, ANOVA, regresión, SPC, fiabilidad, PCA.
- `tests/unit/commands.test.ts`: intérprete de comandos.
- `tests/unit/persistence.test.ts`: proyecto, CSV y HTML.
- `tests/unit/registry.test.ts`: integridad del catálogo de análisis.

## Decisiones registradas

- ADR 0001: arquitectura limpia.
- ADR 0002: motor estadístico en TypeScript puro.
- ADR 0003: gráficos SVG propios.
- ADR 0004: formatos `.ttab`, `.ttw` y CSV.
