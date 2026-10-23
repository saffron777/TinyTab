# ADR 0003 — Gráficos SVG propios

- Estado: aceptado
- Fecha: 2026-10-23

## Contexto

La interfaz debe parecerse a la de Minitab (cartas de control, gráficos de probabilidad, residuos 4-en-1).
Se necesita control fino del aspecto y licencias limpias.

## Decisión

El dominio describe gráficos mediante `ChartSpec` (discriminated union serializable). La capa de
presentación los dibuja con componentes SVG propios (`src/presentation/charts/ChartView.tsx`).

Ventajas: sin dependencias de charting, render determinista, exportable a SVG/PNG en el futuro, y los
gráficos se pueden guardar/reabrir porque el `payload` contiene los datos, no una imagen.

## Consecuencias

- Cada nuevo gráfico requiere un componente SVG y una variante en `ChartSpec`.
- No hay interacción avanzada (zoom/brushing) en v0.1; el diseño lo permite añadir después.
