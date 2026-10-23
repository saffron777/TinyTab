# TinyTab Statistical Software

**Software estadístico libre de escritorio** para Windows, construido con Electron + React + TypeScript.
La experiencia de uso está inspirada en Minitab 22 (hoja de trabajo, ventana de sesión, gestor de proyecto,
menús organizados por familias de análisis y diálogos), pero **todo el código y los recursos gráficos son
originales** y el motor de cálculo es propio.

- **Licencia:** GNU General Public License v3.0 o posterior (`GPL-3.0-or-later`). Uso gratuito, sin coste ni registro.
- **Sin telemetría, sin cuentas, sin licencias.** 100 % local.
- **No afiliado a Minitab, LLC.** “Minitab” es una marca de su titular; este proyecto no incluye ni copia
  binarios, iconos, textos, datos de ejemplo ni código del producto original.

## Estado

Versión **0.1.0**. Consulta [CHANGELOG.md](CHANGELOG.md) y la carpeta `docs/` (arquitectura y ADR).

## Requisitos

- Windows 10/11 x64
- Node.js ≥ 22.12 y npm ≥ 10 (probado con Node 24.21 y npm 11)

## Puesta en marcha

```bash
npm install       # instala dependencias y descarga Electron
npm run dev       # aplicación en modo desarrollo
npm run verify    # typecheck + lint + tests + build
npm test          # 40 pruebas unitarias/de integración (Vitest)
npm run test:coverage
npm run package   # instalador NSIS + portable en release/
```

> Si la red corporativa bloquea la descarga de Electron, define `ELECTRON_MIRROR` o copia el binario
> (`node_modules/electron/dist`) desde otra instalación.

## Arquitectura

Arquitectura limpia (Clean Architecture) en cuatro capas:

```
src/domain          entidades y estadística pura (sin Node, React ni Electron)
src/application     casos de uso, lenguaje de comandos, puertos
src/infrastructure  adaptadores: puente de archivos Electron
src/presentation    React: shell, hoja de trabajo, sesión, diálogos, gráficos SVG
src/main, preload   proceso principal de Electron e IPC seguro
```

Detalle en [docs/architecture.md](docs/architecture.md) y [ADR 0001](docs/adr/0001-clean-architecture.md).

## Estructura funcional (v0.1)

- Hoja de trabajo con columnas numéricas, de texto y fecha; faltantes `*`; edición y renombrado.
- **21 distribuciones** de probabilidad con PDF/PMF, CDF e inversa, y muestreo con semilla.
- **60 análisis** en el catálogo: descriptiva, tablas, pruebas t/Z/proporciones/varianzas, chi-cuadrado,
  Anderson-Darling, no paramétricos, ANOVA, regresión (múltiple, stepwise, subconjuntos, logística,
  Poisson), cartas de control, capacidad, Gage R&R, DOE, series temporales, fiabilidad, multivariante y
  potencia/tamaño de muestra.
- Lenguaje de comandos compatible en subconjunto (`LET`, `SET`, `NAME`, `PRINT`, `DESCRIBE`, `REGRESS`,
  `ANOVA`, `TTEST`, `NOTE`, `HELP`).
- Proyectos `.ttab` (JSON versionado), exportación de hojas a CSV y de la sesión a HTML.
- Interfaz y gráficos propios (histograma, caja, dispersión, cartas de control, probabilidad, residuos,
  series temporales, líneas, barras y PCA) dibujados en SVG.

## Pruebas y verificación

- `tests/unit`: funciones especiales, distribuciones, pruebas estadísticas, comandos, persistencia,
  integridad del catálogo y prueba de humo de los 60 análisis.
- `tests/integration/app.test.tsx`: monta la interfaz completa con Testing Library y ejecuta una
  descriptiva y comandos, comprobando la sesión y la hoja de trabajo.
- Cobertura configurada para el núcleo (dominio + aplicación): líneas ≥ 70 %, funciones ≥ 65 %,
  ramas ≥ 50 %.

## Contribuir

Lee `docs/architecture.md`, respeta la regla de dependencias y ejecuta `npm run verify` antes de cada
commit. Los commits siguen Conventional Commits y el historial se organiza por etapas (ver CHANGELOG).

## Licencia

GPL-3.0-or-later. Texto completo en [LICENSE](LICENSE). Avisos de terceros en [NOTICE](NOTICE).
