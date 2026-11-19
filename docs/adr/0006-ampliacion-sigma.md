# ADR 0006 — Ampliación del StatisticEngine con las capacidades de Sigma

- Estado: aceptado
- Fecha: 2026-11-19
- Complementa: [ADR 0005](0005-microservicio-statisticengine.md)

## Contexto

El microservicio `StatisticEngine` nació como un port 1:1 de los 60 análisis del motor
TypeScript de TinyTab, con paridad verificada por vectores dorados. Sigma Algebra es otro
producto del mismo autor con 154 análisis en 17 módulos (pruebas, modelos, series,
econometría, multivariado, supervivencia, encuestas, DOE, optimización, preparación de
datos, gráficos, ML y SEM), apoyado en scipy, statsmodels, scikit-learn, arch, linearmodels
y semopy.

Se necesita que el servicio HTTP ofrezca esas capacidades sin arrastrar Python al servidor y
sin romper la paridad de los 60 análisis existentes.

## Decisión

1. **Ampliar el catálogo C# con análisis portados desde Sigma** (no un proxy al servicio
   Python). Fases: pruebas/potencia, modelos/ANOVA/encuestas, series/econometría,
   multivariante/supervivencia/calidad/DOE/optimización/remuestreo y datos/gráficos. ML y
   SEM quedan fuera de este alcance.
2. **El baseline TinyTab queda congelado**: los 60 análisis, sus resultados, categorías y
   formato de texto no se modifican. `AnalysisCatalog` separa `Baseline` de `Extensions`;
   los análisis nuevos se publican con `origin = "sigma"`.
3. **Sigma es un oráculo de desarrollo, no una dependencia de ejecución**: un generador
   Python (`tools/sigma-golden`) escribe vectores dorados versionados que las pruebas C#
   consumen sin red ni Python.
4. **Cero dependencias estadísticas nuevas**: el dominio sigue siendo C# puro. Se
   implementan álgebra, optimización, distribuciones, pruebas y modelos propios, como ya se
   hizo con el baseline.
5. **El contrato HTTP crece de forma aditiva**: `columns`/`roles`/`datetime` en análisis,
   secciones `heading`/`tiles`/`alert`, `datasets`, `keyResults` y el grupo `/api/v1/data`
   para frames tipados. La versión pasa a `2.0.0` y se publica `extensionsBaseline`.
6. **Los gráficos son datos, no Plotly**: se añaden variantes de `ChartSpec` y el cliente
   dibuja. Los análisis con RNG se validan por propiedades (xoshiro128 ≠ PCG64).

## Alternativas descartadas

- **Sidecar Python/Flask con Sigma**: duplicaría el runtime, añadiría latencia y acoplaría
  el servicio a Python, justo lo que el ADR 0005 evitó con Node.
- **ML.NET/MathNet para acelerar**: rompería la política del ADR 0005 y haría inviable la
  comparación numérica con Sigma; además añade dependencias con otras licencias.
- **Duplicar análisis ya cubiertos**: se documenta el mapeo y sólo se añaden las
  capacidades que faltan (p. ej. QDA, X̄-S, potencia de correlación/TOST/pareada).
- **Fijar los 60 análisis y crear un servicio aparte**: obligaría a dos catálogos y dos
  contratos; se prefiere un catálogo con `origin`.

## Consecuencias

- El catálogo crece por fases; cada fase es un entregable verificable con sus vectores y
  pruebas. El baseline sigue siendo la referencia de no regresión.
- Hay que mantener dos baselines documentados: `tinytab@0.1.0/src/domain` y
  `sigma@5c378d7`. `PARITY-NOTES.md` cubre el primero; `EXTENSIONS-NOTES.md`, el segundo.
- Los mensajes de validación de los análisis extendidos replican los de Sigma; los 60
  baseline conservan los suyos.
- El servicio gana operaciones de datos con frames tipados, lo que exige límites y
  timeouts nuevos, pero mantiene el estado cero y la autosuficiencia del contenedor.
- Licencia: Sigma es AGPL-3.0-or-later y el servicio GPL-3.0-or-later. El port se ampara en
  que el titular de ambos proyectos es el mismo; en caso contrario habría que revisar la
  compatibilidad antes de copiar código.
