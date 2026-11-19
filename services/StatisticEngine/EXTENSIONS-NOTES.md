# Extensiones Sigma del StatisticEngine

Este documento describe cómo se amplía el motor C# con las capacidades analíticas de
**Sigma Algebra** (`D:\IA\Sigma`, baseline `sigma@5c378d7`) y qué significa "paridad" para
los análisis extendidos. El baseline TinyTab (60 análisis) sigue congelado y verificado
contra el motor TypeScript; ver `PARITY-NOTES.md`.

## 1. Método

1. `tools/sigma-golden/generate.py` ejecuta el registro real de Sigma con el fixture
   `tests/analysis_data.py` y escribe, por cada análisis:
   - el **ejemplo declarado** (`spec.example`) con sus roles y parámetros;
   - los **casos de error** de validación (rol obligatorio omitido y select inválido);
   - los **metadatos** (label, grupo, descripción, roles, parámetros, opciones, outputs, heavy).
2. Los vectores se guardan en `tests/StatisticEngine.Domain.Tests/Extensions/vectors/`
   (un archivo por módulo, más `fixtures.json` y `extensions-catalog.json`) y se versionan.
3. `ExtensionParityTests` compara el motor C# contra esos vectores; `SigmaCatalogCompare`
   compara la declaración del catálogo.

Sigma sólo se usa **en desarrollo**: el servicio no ejecuta Python ni conoce su ruta en runtime.

### Regenerar vectores

```powershell
D:\IA\Sigma\.venv\Scripts\python.exe services\StatisticEngine\tools\sigma-golden\generate.py `
  --sigma-root D:\IA\Sigma `
  --out services\StatisticEngine\tests\StatisticEngine.Domain.Tests\Extensions\vectors
```

Se puede limitar con `--modules pruebas potencia` mientras se trabaja una fase.

## 2. Reglas de comparación

| Elemento | Regla |
| --- | --- |
| Título, subtítulo, títulos de bloque, cabeceras, etiquetas de tiles | exactos |
| Textos de conclusión y mensajes de error | exactos (normalizando espacios) |
| Celdas y tiles numéricos | se parsean del texto del motor y se comparan con tolerancia |
| `keyResults` | número crudo con tolerancia relativa `1e-5` |
| Datasets | nombres/tipos exactos; valores con tolerancia `1e-6` |
| Gráficos | se compara el número de gráficos (el dibujo es responsabilidad del cliente) |
| Descripciones/opciones/roles/parámetros | exactos contra el spec de Sigma |

Tolerancias de tablas: `2e-4` relativo porque el motor formatea con 4 decimales
(`N`) frente a los números crudos de Sigma; los p-valores `< 0.001` se aceptan como
"menor que el límite". Los ids con RNG (fases 4-5) se marcarán `deterministic: false`
y se validarán por propiedades, no por igualdad.

## 3. Cobertura y fases

| Fase | Contenido | Estado |
| --- | --- | --- |
| 0 | Infraestructura: catálogo con origen, roles/columnas tipadas, secciones/datasets, núcleo numérico, generador Sigma | completada (salvo documentación generada) |
| 1 | 18 pruebas adicionales (normalidad, varianzas, no paramétricas, tasas/tablas, acuerdo, multivariadas, IC) + 4 de potencia | completada |
| 2 | 17 modelos de regresión, 5 ANOVA avanzados, 2 encuestas | en curso: 19/24 (`regression-wls`, `regression-polynomial`, `regression-robust`, `regression-glm`, `regression-count`, `survey-ols`, `survey-logistic`, `regression-quantile`, `regression-diagnostics`, `regression-loglinear`, `regression-iv`, `regression-pls`, `regression-probit`, `regression-nonlinear`, `regression-ordinal`, `regression-regularized`, `anova-posthoc`, `anova-nested`, `anova-repeated-measures`) |
| 3 | 11 series temporales, 12 econometría | en curso: 12/23 (series: `ts-plot`, `trend`, `ccf`, `mann-kendall`, `periodogram`, `filters`, `decompose`, `acf-pacf`, `backtest`; econometría: `copula`, `granger`, `bai-perron`) |
| 4 | 8 multivariante, 3 supervivencia, 2 calidad, 2 DOE, 1 optimización, 4 remuestreo | en curso: 13/20 (`mahalanobis`, `mds`, `hierarchical`, `reliability`, `bootstrap`, `permutation`, `monte-carlo`, `random-data`, `lp`, `factorial`, `response-surface`, `parametric`, `cox`) |
| 5 | 13 gráficos y 23 operaciones de datos | en curso: 9/13 gráficos y 4/23 operaciones (`data-standardize`, `data-sort`, `data-select-columns`, `data-duplicates`) |

Mapeo fino de cada módulo y motivos de cobertura: ver el plan aprobado y la tabla de
`AnalysisCatalog.Extensions`.

### Ids de la fase 1

`anderson-darling`, `lilliefors`, `ks-one-sample`, `ks-two-samples`, `jarque-bera`,
`variance-equality-tests`, `mood-median`, `friedman`, `mcnemar`, `cochran-q`,
`poisson-rate`, `odds-ratio`, `mantel-haenszel`, `kappa`, `icc`, `hotelling-t2`,
`partial-correlation`, `confidence-interval`, `power-correlation`, `power-tost`,
`power-paired-t`, `power-prop2`.

Capacidades de Sigma ya cubiertas por el baseline y por tanto no duplicadas:
`sign_test`, `runs`, `tost`, `t_power` (salvo pareada, publicada como `power-paired-t`),
`prop_power` (salvo dos proporciones, `power-prop2`), `anova_power`,
`control_chart` (salvo X̄-S, fase 4), `capability`, `kaplan_meier`, `pca`, `factor`,
`kmeans`, `discriminant` (salvo QDA, fase 4), `stepwise`, `best_subsets` y `binary`
(la logística ya existe; `regression-probit` se añadirá con el GLM binomial).

### Ids de la fase 2 ya implementados

`regression-wls`, `regression-polynomial`, `regression-robust` (HC0/HC1/HC3/HAC; M queda
pendiente), `regression-glm` (gaussian/binomial/poisson/gamma/gaussiana inversa),
`regression-count` (Poisson y cuasi-Poisson; negbin pendiente), `survey-ols`,
`regression-quantile`, `regression-diagnostics` (Breusch-Pagan y Durbin-Watson; el resto de
pruebas devuelve `NOT_SUPPORTED`), `regression-loglinear`, `regression-iv` (2MCO con primera
etapa, Wu-Hausman y Sargan), `regression-pls` (NIPALS1 compatible con sklearn) y
`regression-probit` (binaria con clasificación, ROC y Hosmer-Lemeshow), `regression-nonlinear`
(modelos incorporados con Gauss-Newton; la fórmula personalizada queda pendiente),
`regression-ordinal` (probabilidades proporcionales logit/probit por máxima verosimilitud),
`regression-regularized` (lasso/ridge/red elástica con validación cruzada; al ser no
determinista se valida con una prueba de humo y no con igualdad exacta de vector),
`survey-logistic` (logística ponderada por pesos muestrales, AIC/BIC con suma de pesos),
`anova-posthoc` (Bonferroni, Holm, Šidák, Scheffé y LSD; Tukey/Games-Howell/Dunnett
pendientes), `anova-nested` (sumas de cuadrados secuenciales y componentes de la varianza) y
`anova-repeated-measures` (Mauchly, Greenhouse-Geisser, Huynh-Feldt y comparaciones
pareadas). Fase 3 iniciada con `ts-plot`, `trend`, `ccf`, `mann-kendall`, `periodogram`,
`filters`, `decompose`, `acf-pacf`, `backtest`, `copula` y `granger` (exploración, tendencia,
correlación cruzada, Mann-Kendall, periodograma, Hodrick-Prescott, descomposición clásica
aditiva, ACF/PACF con Ljung-Box, validación de origen móvil, cópula Gaussiana, causalidad de
Granger con pruebas F y χ² por rezago y quiebres múltiples de Bai-Perron por programación
dinámica con BIC/LWZ). Fase 4 iniciada con `mahalanobis` (distancias clásicas,
contraste χ², atípicos, tabla de las observaciones más alejadas y dataset de distancias; MCD
robusto pendiente), `bootstrap` (intervalo percentil de media/mediana/desviación/percentil),
`permutation` (diferencias de medias/medianas, pareados y correlación), `monte-carlo`
(simulación de la distribución muestral con 11 distribuciones y 7 estadísticos) y
`random-data` (generación de datasets con 11 distribuciones) y `mds` (escalamiento
multidimensional clásico con valores propios, varianza reproducida, estrés, mapa y
coordenadas) y `hierarchical` (Ward con silueta, correlación cofenética, perfiles y
diferencias), además de `reliability` (alfa, alfa estandarizado, omega por factor ML, análisis
de ítems, dos mitades y error estándar de medida). Fase 5 iniciada con `splom`, `heatmap`,
`line-plot`, `pie`, `pareto`, `scatter3d`, `interval`, `scatter` y `probability` (ajuste
normal con KS, Anderson-Darling y gráfico Q-Q), `lp` (asignación con presupuesto), `factorial`
(diseño 2^k) y `response-surface` (compuesto central), y `parametric` (Weibull con censura por
verosimilitud, Nelder-Mead equivalente a scipy, mediana/media y curva ajustada) y `cox`
(riesgos proporcionales con ties de Efron, razones de riesgo, IC y valores p). El ejemplo
dorado de `regression-robust` usa HC3, el de `count` usa Poisson, el de `diagnostics` usa BP +
DW y el de `posthoc` usa Bonferroni: los métodos M, la binomial negativa, las pruebas
White/RESET/Breusch-Godfrey/Shapiro/CUSUM/Chow y el rango estudentizado se portarán en la
siguiente ronda.

## 4. Desviaciones documentadas

- **Gráficos**: Sigma genera figuras Plotly; el motor emite `ChartSpec` de datos y deja el
  dibujo al cliente. No se comparan trazas en las pruebas.
- **Formato de tablas**: Sigma guarda números crudos y elige el formato en el informe; el
  motor usa `N` (4 decimales) y `P` como el resto del catálogo. Por eso las comparaciones
  numéricas de tablas usan `2e-4`.
- **RNG**: los análisis con aleatoriedad usarán xoshiro128, no PCG64 de NumPy; no se buscará
  igualdad bit a bit (fases 4-5).
- **KS de una cola**: se usa la aproximación `2·P(D ≥ d)` de la cola bilateral (scipy usa
  `ksone.sf` exacta); sólo afecta a `alternative != two-sided` y se revisará si se añaden
  vectores con esa opción.
- **Anderson-Darling logística/Gumbel**: el ajuste de parámetros usa método de momentos, no
  el `fsolve` de scipy; el ejemplo verificado usa la normal.
- **Lilliefors**: tabla de statsmodels (normal y exponencial) para n ≤ 1600; para n mayores
  se recorta a la última fila (pendiente de la asíntota).
- **TOST/TOST power y t pareada**: integración numérica adaptativa en lugar de `quad`/`nct`
  de scipy; diferencias esperadas < 1e-9 en los casos verificados.

## 5. Cómo añadir una fase

1. Copiar la declaración del spec de Sigma a `Analysis/Analyses/Extensions/`.
2. Implementar el cálculo con los núcleos de `Domain/` (sin dependencias nuevas).
3. Añadir la lista a `ExtensionCatalog` (o a `DataCatalog` para operaciones de datos).
4. Regenerar/crear vectores y ejecutar:

```powershell
dotnet test services/StatisticEngine/StatisticEngine.slnx -c Release --filter "FullyQualifiedName~Extensions"
```
