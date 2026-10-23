# ADR 0001 — Arquitectura limpia para TinyTab

- Estado: aceptado
- Fecha: 2026-10-23
- Contexto: TinyTab es una aplicación de escritorio con Electron + React que debe crecer por etapas,
  mantener un motor estadístico verificable y no acoplar el cálculo a la interfaz.

## Decisión

Se adopta Clean Architecture con cuatro capas y una regla de dependencia estricta:

```
presentation → application → domain
main / infrastructure → ports de application
```

- `src/domain`: entidades (`Column`, `Worksheet`, `ProjectDoc`), tipos de resultado y todo el cálculo
  estadístico como funciones puras. No importa Node, React, Electron, Zustand ni código de UI.
- `src/application`: casos de uso (`run-analysis`, `import-csv`, `save-project`, `execute-command`) y
  puertos (`AnalysisRunner`, `ProjectRepository`, `FileGateway`). Orquesta el dominio sin conocer
  detalles de Electron ni de la vista.
- `src/infrastructure`: adaptadores de los puertos (sistema de archivos, worker de análisis, CSV).
- `src/presentation`: React, Zustand, componentes y estilos. Sólo invoca casos de uso.
- `src/main` y `src/preload`: proceso principal de Electron e IPC con `contextIsolation`. Toda la
  validación de límites ocurre en `src/shared`.

La regla de dependencias se comprueba con ESLint (`no-restricted-imports` por carpeta) y con la
revisión de PR. Los tests de dominio no requieren Electron ni DOM.

## Consecuencias

- El dominio es testeable y portable (podría ejecutarse en un worker o en Node sin cambios).
- Los análisis pesados se ejecutan fuera del hilo de UI mediante el puerto `AnalysisRunner`.
- Añadir un análisis consiste en: función de dominio + tipo de petición/resultado + entrada en el
  registro + caso de uso (si aplica) + diálogo/entrada de menú.
