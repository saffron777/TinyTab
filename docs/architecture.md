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

## Microservicio StatisticEngine (C# / .NET 10)

El motor estadístico del dominio se expone además como microservicio HTTP independiente en
`services/StatisticEngine`, con un port nativo en C# verificado 1:1 contra el motor TypeScript
mediante vectores dorados:

```
services/StatisticEngine/src/StatisticEngine.Domain        cálculo puro (espeja src/domain)
services/StatisticEngine/src/StatisticEngine.Application   casos de uso y puertos
services/StatisticEngine/src/StatisticEngine.Api           host HTTP, contratos y límites
services/StatisticEngine/tools/golden                      generación de vectores de paridad
```

- Regla de dependencia: `Api → Application → Domain`; el dominio no conoce HTTP ni I/O.
- API versionada `/api/v1`: catálogo y ejecución de los 60 análisis, 21 distribuciones, metadatos
  y probes de salud. Errores en `application/problem+json` con `code` del motor.
- Sin estado ni base de datos: el RNG recibe semilla explícita y cada petición es independiente.
- Paridad verificada (29 pruebas de dominio + 19 de API) contra el motor TypeScript, incluidos sus
  defectos documentados. Detalle en `services/StatisticEngine/PARITY-NOTES.md`.
- La aplicación de escritorio sigue usando el motor TypeScript; el servicio es autónomo.

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
- ADR 0005: microservicio `StatisticEngine` en C# / .NET 10 con paridad verificada.
