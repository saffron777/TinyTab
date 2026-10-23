# Changelog

Todas las etapas relevantes del proyecto se documentan aquí. El formato sigue
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y el versionado
[SemVer](https://semver.org/lang/es/).

## [0.1.0] - 2026-10-23

Primera versión pública de TinyTab Statistical Software. Licencia GPL-3.0-or-later.

### Añadido

- **Etapa 0 — Estructura**: Electron 45 + React 19 + TypeScript 5.9 con electron-vite, ESLint,
  Prettier, Vitest y electron-builder; licencia GPLv3, NOTICE, README, CI y `pnpm verify`.
- **Etapa 1 — Modelo**: `Column`, `Worksheet`, `ProjectDoc`, celdas tipadas con faltantes `*`,
  `Result` explícito y utilidades numéricas (cuantiles, momentos, funciones especiales).
- **Etapa 2 — Estadística descriptiva y distribuciones**: 21 distribuciones con pdf/cdf/invCdf y
  muestreo, RNG xoshiro128** determinista, tablas de frecuencia, descriptiva e intervalos de confianza.
- **Etapa 3 — Inferencia**: pruebas t/Z, proporciones, varianzas, chi-cuadrado, Anderson-Darling,
  equivalencia TOST, ANOVA de uno y dos factores, comparaciones Holm, regresión múltiple, stepwise,
  mejores subconjuntos, logística y Poisson, potencia y tamaño de muestra.
- **Etapa 4 — Calidad y modelos**: cartas X̄-R, I-MR, P, C, U, EWMA, CUSUM y reglas de detección;
  capacidad Cp/Cpk/Pp/Ppk; Gage R&R; análisis factorial de efectos; ACF/PACF, suavizados, descomposición,
  AR y Ljung-Box; Weibull y Kaplan-Meier; PCA, factorial, k-means y LDA; no paramétricos.
- **Etapa 5 — Catálogo de análisis**: registro declarativo con 60 análisis, metadatos para generar
  diálogos y formateo de resultados para la ventana de sesión.
- **Etapa 6 — Aplicación**: puertos (`AnalysisRunner`, `FileGateway`), casos de uso de análisis,
  persistencia y CSV; lenguaje de comandos Minitab-like (`NOTE`, `NAME`, `PRINT`, `DESCRIBE`, `LET`,
  `SET`, `TTEST`, `ANOVA`, `REGRESS`, `HELP`) con lexer, parser de expresiones e intérprete.
- **Etapa 7 — Interfaz**: shell estilo Minitab (barra de menús, toolbar, gestor de proyecto, hoja de
  trabajo editable, ventana de sesión, barra de estado), tema claro y atajos de teclado.
- **Etapa 8 — Diálogos y gráficos**: diálogo genérico dirigido por el registro de análisis, editor de
  comandos, ayuda/acerca de, y gráficos SVG propios (histograma, caja, dispersión, control,
  probabilidad, residuos, series temporales, líneas, barras y PCA).
- **Etapa 9 — Archivos**: proyecto `.ttab`, hoja `.ttw`/CSV, importación con detección de delimitador,
  exportación de sesión a HTML.
- **Etapa 10 — Documentación y pruebas**: `docs/architecture.md`, ADR 0001–0004, pruebas unitarias de
  dominio, comandos, persistencia y registro, y empaquetado NSIS/portable.

### Limitaciones conocidas

- Cobertura de análisis orientada a la práctica habitual; no se replican los 388 diálogos de Minitab.
- Potencia y ANOVA usan aproximaciones normales documentadas.
- Sin zoom/brushing interactivo en gráficos.
- El formato de proyecto no es compatible con archivos de Minitab.

[0.1.0]: https://example.org/tinytab/releases/tag/v0.1.0
