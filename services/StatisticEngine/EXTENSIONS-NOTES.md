# Extensiones Sigma del StatisticEngine

Este documento describe c�mo se ampl�a el motor C# con las capacidades anal�ticas de
**Sigma Algebra** (`D:\IA\Sigma`, baseline `sigma@5c378d7`) y qu� significa "paridad" para
los an�lisis extendidos. El baseline TinyTab (60 an�lisis) sigue congelado y verificado
contra el motor TypeScript; ver `PARITY-NOTES.md`.

## Estado actual (ronda 55)

- **60/60 análisis baseline** intactos y verificados.
- **102/102 análisis Sigma activos**. Noventa y seis tienen paridad numérica exacta; `markov`, `ucm`, `garch`, `regression-mixed`, `smoothing` y `arima` se validan por propiedades porque su referencia depende de RNG no sembrado o de puntos de parada de optimizadores.
- **13/13 gráficos** y **23/23 operaciones de datos** completados.
- **136 análisis Sigma catalogados** en 15 módulos y 17 archivos de vectores instalados.
- Build Release con `-warnaserror` y **52 pruebas de dominio + 25 de API** en verde.
- Documentación final regenerada: `API.md`, `.http` y Postman cubren los 162 análisis y 21 distribuciones. Sólo `gage-rr` queda documentado con su error heredado.

## 1. M�todo

1. `tools/sigma-golden/generate.py` ejecuta el registro real de Sigma con el fixture
   `tests/analysis_data.py` y escribe, por cada an�lisis:
   - el **ejemplo declarado** (`spec.example`) con sus roles y par�metros;
   - los **casos de error** de validaci�n (rol obligatorio omitido y select inv�lido);
   - los **metadatos** (label, grupo, descripci�n, roles, par�metros, opciones, outputs, heavy).
2. Los vectores se guardan en `tests/StatisticEngine.Domain.Tests/Extensions/vectors/`
   (un archivo por m�dulo, m�s `fixtures.json` y `extensions-catalog.json`) y se versionan.
3. `ExtensionParityTests` compara el motor C# contra esos vectores; `SigmaCatalogCompare`
   compara la declaraci�n del catálogo.

Sigma s�lo se usa **en desarrollo**: el servicio no ejecuta Python ni conoce su ruta en runtime.

### Regenerar vectores

```powershell
D:\IA\Sigma\.venv\Scripts\python.exe services\StatisticEngine\tools\sigma-golden\generate.py `
  --sigma-root D:\IA\Sigma `
  --out services\StatisticEngine\tests\StatisticEngine.Domain.Tests\Extensions\vectors
```

Se puede limitar con `--modules pruebas potencia` mientras se trabaja una fase.

## 2. Reglas de comparaci�n

| Elemento | Regla |
| --- | --- |
| T�tulo, subt�tulo, t�tulos de bloque, cabeceras, etiquetas de tiles | exactos |
| Textos de conclusi�n y mensajes de error | exactos (normalizando espacios) |
| Celdas y tiles num�ricos | se parsean del texto del motor y se comparan con tolerancia |
| `keyResults` | n�mero crudo con tolerancia relativa `1e-5` |
| Datasets | nombres/tipos exactos; valores con tolerancia `1e-6` |
| Gr�ficos | se compara el n�mero de gráficos (el dibujo es responsabilidad del cliente) |
| Descripciones/opciones/roles/par�metros | exactos contra el spec de Sigma |

Tolerancias de tablas: `2e-4` relativo porque el motor formatea con 4 decimales
(`N`) frente a los n�meros crudos de Sigma; los p-valores `< 0.001` se aceptan como
"menor que el l�mite". Los ids con RNG (fases 4-5) se marcar�n `deterministic: false`
y se validar�n por propiedades, no por igualdad.

## 3. Cobertura y fases

| Fase | Contenido | Estado |
| --- | --- | --- |
| 0 | Infraestructura: catálogo con origen, roles/columnas tipadas, secciones/datasets, núcleo num�rico, generador Sigma | completada (salvo documentación generada) |
| 1 | 18 pruebas adicionales (normalidad, varianzas, no param�tricas, tasas/tablas, acuerdo, multivariadas, IC) + 4 de potencia | completada |
| 2 | 17 modelos de regresión, 5 ANOVA avanzados, 2 encuestas | en curso: 21/24 (añadidos `regression-multinomial` y `regression-gam`; `regression-mixed` implementado pero bloqueado por el oráculo —§8.5—; pendientes ANOVA factorial y MANOVA) |
| 3 | 11 series temporales, 12 econometría | en curso: 13/23 (series: `ts-plot`, `trend`, `ccf`, `mann-kendall`, `periodogram`, `filters`, `decompose`, `acf-pacf`, `backtest`; econometría: `copula`, `granger`, `bai-perron`, `qa-break`) |
| 4 | 8 multivariante, 3 supervivencia, 2 calidad, 2 DOE, 1 optimización, 4 remuestreo | en curso: 16/20 (`cca`, `correspondence` y `mixture` añadidos; pendiente calidad) |
| 5 | 13 gráficos y 23 operaciones de datos | **completada** (13/13 gráficos y 23/23 operaciones) |

Mapeo fino de cada m�dulo y motivos de cobertura: ver el plan aprobado y la tabla de
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
`control_chart` (salvo X?-S, fase 4), `capability`, `kaplan_meier`, `pca`, `factor`,
`kmeans`, `discriminant` (salvo QDA, fase 4), `stepwise`, `best_subsets` y `binary`
(la log�stica ya existe; `regression-probit` se a�adir� con el GLM binomial).

### Ids de la fase 2 ya implementados

`regression-wls`, `regression-polynomial`, `regression-robust` (HC0/HC1/HC3/HAC; M queda
pendiente), `regression-glm` (gaussian/binomial/poisson/gamma/gaussiana inversa),
`regression-count` (Poisson y cuasi-Poisson; negbin pendiente), `survey-ols`,
`regression-quantile`, `regression-diagnostics` (Breusch-Pagan y Durbin-Watson; el resto de
pruebas devuelve `NOT_SUPPORTED`), `regression-loglinear`, `regression-iv` (2MCO con primera
etapa, Wu-Hausman y Sargan), `regression-pls` (NIPALS1 compatible con sklearn) y
`regression-probit` (binaria con clasificaci�n, ROC y Hosmer-Lemeshow), `regression-nonlinear`
(modelos incorporados con Gauss-Newton; la f�rmula personalizada queda pendiente),
`regression-ordinal` (probabilidades proporcionales logit/probit por m�xima verosimilitud),
`regression-regularized` (lasso/ridge/red el�stica con validaci�n cruzada; al ser no
determinista se valida con una prueba de humo y no con igualdad exacta de vector),
`survey-logistic` (log�stica ponderada por pesos muestrales, AIC/BIC con suma de pesos),
`anova-posthoc` (Bonferroni, Holm, Sid�k, Scheff� y LSD; Tukey/Games-Howell/Dunnett
pendientes), `anova-nested` (sumas de cuadrados secuenciales y componentes de la varianza) y
`anova-repeated-measures` (Mauchly, Greenhouse-Geisser, Huynh-Feldt y comparaciones
pareadas). Fase 3 iniciada con `ts-plot`, `trend`, `ccf`, `mann-kendall`, `periodogram`,
`filters`, `decompose`, `acf-pacf`, `backtest`, `copula` y `granger` (exploraci�n, tendencia,
correlaci�n cruzada, Mann-Kendall, periodograma, Hodrick-Prescott, descomposici�n cl�sica
aditiva, ACF/PACF con Ljung-Box, validaci�n de origen m�vil, c�pula Gaussiana, causalidad de
Granger con pruebas F y ?� por rezago y quiebres m�ltiples de Bai-Perron por programaci�n
din�mica con BIC/LWZ). Fase 4 iniciada con `mahalanobis` (distancias cl�sicas,
contraste ?�, at�picos, tabla de las observaciones m�s alejadas y dataset de distancias; MCD
robusto pendiente), `bootstrap` (intervalo percentil de media/mediana/desviaci�n/percentil),
`permutation` (diferencias de medias/medianas, pareados y correlaci�n), `monte-carlo`
(simulaci�n de la distribuci�n muestral con 11 distribuciones y 7 estad�sticos) y
`random-data` (generaci�n de datasets con 11 distribuciones) y `mds` (escalamiento
multidimensional cl�sico con valores propios, varianza reproducida, estr�s, mapa y
coordenadas) y `hierarchical` (Ward con silueta, correlaci�n cofen�tica, perfiles y
diferencias), adem�s de `reliability` (alfa, alfa estandarizado, omega por factor ML, an�lisis
de �tems, dos mitades y error est�ndar de medida). Fase 5 iniciada con `splom`, `heatmap`,
`line-plot`, `pie`, `pareto`, `scatter3d`, `interval`, `scatter` y `probability` (ajuste
normal con KS, Anderson-Darling y gr�fico Q-Q), `lp` (asignaci�n con presupuesto), `factorial`
(dise�o 2^k) y `response-surface` (compuesto central), y `parametric` (Weibull con censura por
verosimilitud, Nelder-Mead equivalente a scipy, mediana/media y curva ajustada) y `cox`
(riesgos proporcionales con ties de Efron, razones de riesgo, IC y valores p). El ejemplo
dorado de `regression-robust` usa HC3, el de `count` usa Poisson, el de `diagnostics` usa BP +
DW y el de `posthoc` usa Bonferroni: los m�todos M, la binomial negativa, las pruebas
White/RESET/Breusch-Godfrey/Shapiro/CUSUM/Chow y el rango estudentizado se portar�n en la
siguiente ronda.

## 4. Desviaciones documentadas

- **Gr�ficos**: Sigma genera figuras Plotly; el motor emite `ChartSpec` de datos y deja el
  dibujo al cliente. No se comparan trazas en las pruebas.
- **Formato de tablas**: Sigma guarda n�meros crudos y elige el formato en el informe; el
  motor usa `N` (4 decimales) y `P` como el resto del catálogo. Por eso las comparaciones
  num�ricas de tablas usan `2e-4`.
- **RNG**: los an�lisis con aleatoriedad usar�n xoshiro128, no PCG64 de NumPy; no se buscar�
  igualdad bit a bit (fases 4-5).
- **KS de una cola**: se usa la aproximaci�n `2�P(D ? d)` de la cola bilateral (scipy usa
  `ksone.sf` exacta); s�lo afecta a `alternative != two-sided` y se revisar� si se a�aden
  vectores con esa opci�n.
- **Anderson-Darling log�stica/Gumbel**: el ajuste de par�metros usa m�todo de momentos, no
  el `fsolve` de scipy; el ejemplo verificado usa la normal.
- **Lilliefors**: tabla de statsmodels (normal y exponencial) para n ? 1600; para n mayores
  se recorta a la �ltima fila (pendiente de la as�ntota).
- **TOST/TOST power y t pareada**: integraci�n num�rica adaptativa en lugar de `quad`/`nct`
  de scipy; diferencias esperadas < 1e-9 en los casos verificados.

## 5. C�mo a�adir una fase

1. Copiar la declaraci�n del spec de Sigma a `Analysis/Analyses/Extensions/`.
2. Implementar el c�lculo con los núcleos de `Domain/` (sin dependencias nuevas).
3. A�adir la lista a `ExtensionCatalog` (o a `DataCatalog` para operaciones de datos).
4. Regenerar/crear vectores y ejecutar:

```powershell
dotnet test services/StatisticEngine/StatisticEngine.slnx -c Release --filter "FullyQualifiedName~Extensions"
```

## 6. Bloqueos de paridad: `markov` y `ucm`

Ambos análisis se prototiparon y se midieron contra el vector; **no se registraron** porque su
resultado de Sigma no es reproducible y ninguna implementación puede cumplir la tolerancia de
datasets (`1e-6`). El bloqueo es del oráculo, no del motor C#.

### 6.1 `markov`: el vector es una realización aleatoria irrepetible

1. `econ_models._markov` hace `np.random.seed(seed)` y luego
   `MarkovRegression(...).fit(search_reps=5)`. En statsmodels 0.15.0 la búsqueda de valores
   iniciales usa `check_random_state(None)`, que **crea un `Generator` nuevo con entropía del
   sistema operativo**: la semilla del catálogo (`seed = 123`) no interviene y la búsqueda no
   es reproducible. Dos ajustes seguidos en el mismo intérprete dan parámetros distintos.
2. Por eso el propio vector cambió al regenerarse en esta ronda: `llf` pasó de
   `-336.21746631686625` (HEAD) a `-336.2174663152933` (árbol de trabajo).
3. Los parámetros del vector (`p[0->0] = 0.9604223337113597`, …) no son el máximo de
   verosimilitud: son el punto donde BFGS se detuvo. La superficie es una cresta casi plana:
   - el óptimo ajustado con precisión da `llf = -336.2174663146071` (+6.9e-10) y se aleja
     `2.16e-6` en las probabilidades suavizadas;
   - perturbar el punto de partida de BFGS en `1e-12` cambia el punto final en `2.5e-5`
     (las decisiones de la búsqueda lineal son discretas), muy por encima de `1e-6`;
   - el EM interno de statsmodels tampoco es monótono (es sólo para valores iniciales).
4. Consecuencia medida con un prototipo del filtro de Hamilton y del suavizador de Kim
   (log-verosimilitud y probabilidades suavizadas idénticas a statsmodels a `3e-15` en un
   punto fijo de parámetros): con el óptimo propio, `llf`/AIC/BIC, `means`, `durations`,
   `transition`, las tablas (tolerancia `2e-4`) y los `keyResults` (`1e-5`) coinciden, pero
   **3 de 480 celdas del dataset `regimenes` difieren 1.3e-6 – 2.2e-6** y la prueba queda roja.
5. Para desbloquearlo hace falta un protocolo determinista en Sigma (por ejemplo EM/BFGS
   convergentes con inicio fijo, sin búsqueda aleatoria) **y regenerar el vector** `markov`;
   después se puede portar el análisis y registrarlo como los demás.

### 6.2 `ucm`: MLE de espacio de estados con varianzas en la frontera

Además del mismo problema de fondo (BFGS de statsmodels sobre una superficie mal condicionada),
`ucm` publica varianzas en la frontera (`sigma2.trend = 2.17e-11`, `sigma2.seasonal = 3.86e-11`)
y estados suavizados cuyo valor depende de la inicialización difusa y de la ruta de BFGS.
Reproducir esos números exige emular el optimizador y el filtro de statsmodels bit a bit; no se
registró. Queda pendiente hasta que exista un vector generado con un procedimiento estable.

### 6.3 `qa-break` sí es reproducible

`andrews_p_value` fija `np.random.default_rng(20240601)`, rejilla 600 y 4 000 réplicas, así que
es determinista. El motor reproduce PCG64 (`SeedSequence` + XSL-RR 128/64) y la normal por
ziggurat de NumPy 2.5.3 en `Numerics/NumpyRandom.cs` + `Numerics/NumpyZigguratTables.cs`; el
valor p (`0.00375`), el crítico 5 % (`8.572955094891606`) y el quiebre (`164`, `2013-08-01`)
coinciden exactamente.

## 7. Sistemas de series: `var`, `cointegration`, `ardl` y `panel` en paridad

Ronda 54. Se añadieron `Analysis/Analyses/Extensions/VarAnalyses.cs`,
`CointegrationAnalyses.cs`, `CointegrationTables.cs` y `MultiSeriesLoader.cs`, más
`Numerics/CholeskyDecomposition.cs` (Cholesky, log-determinante y eigenvalores de una matriz real
general por Hessenberg + QR con desplazamiento de Wilkinson). `var` y `cointegration` se
registraron en `EconometricsAnalyses.All` y sus tres vectores (ejemplo, dos errores de validación
y metadatos) coinciden al 100 % con Sigma.

- `var` reproduce `statsmodels.tsa.api.VAR`: selección de rezagos con la muestra común
  (`offset = max_lags − p`), criterios AIC/BIC/HQIC/FPE con `σ²` MLE y `FPE = ((T + kp)/ (T − kp − 1))^k·|Σ̂|`
  sobre los parámetros por ecuación, coeficientes/errores/t/p (p-valores **normales**, como
  `VARResults.pvalues`), R² y desviación residual con `ddof = k·p + 1`, causalidad de Granger con
  la restricción de Wald (`gl₂ = k·df_resid`), Portmanteau (`trace(Cᵗ Σ₀⁻¹ C Σ₀⁻¹)`), Jarque-Bera
  multivariante con estandarización por Cholesky, raíces del acompañante (se publica el módulo
  mínimo), impulso-respuesta ortogonalizada, FEVD acumulada, pronósticos con su intervalo y el
  dataset `residuos`. La tabla de selección usa la muestra común y el AIC del modelo final usa su
  propia muestra: son números distintos y ambos se reproducen.
- `cointegration` reproduce `coint_johansen(det_order, k_ar_diff)` de
  `statsmodels.tsa.vector_ar.vecm` paso a paso (detrend por `vander(linspace(−1,1,n), orden+1)`,
  `lagmat` recortado, residualización de `z` y de las diferencias, `skk/sk0/s00`, autovalores de
  `skk⁻¹Σ` vía la reducción simétrica `L⁻¹ΣL⁻ᵀ`, normalización de vectores y estadísticos de traza
  y máximo autovalor) y `statsmodels.tsa.stattools.coint` (regresión de nivel con constante, ADF
  sin deterministas con selección de rezagos por AIC y p-valor/valores críticos de MacKinnon 1994
  y 2010). Las tablas de MacKinnon y de Johansen (MacKinnon-Haug-Michelis) se generaron desde
  `statsmodels.tsa.adfvalues` y `statsmodels.tsa.coint_tables` en `CointegrationTables.cs`, de modo
  que los valores críticos son los mismos números.
- `ardl` reproduce `statsmodels.tsa.ardl`: `ardl_select_order` (rejilla sobre todos los órdenes con
  la muestra común recortada por el rezago máximo, criterio calculado con la verosimilitud MLE:
  `-2·llf + penalización·(parámetros + 1)`), `ARDL.fit` (R² **sin centrar**, porque el modelo no
  declara constante propia: `1 − SSR/Σy²`), `UECM.from_ardl` (Δy sobre [deterministas, y.L1, x.L1,
  Δy y Δx rezagados]) y `bounds_test` con la restricción de Wald sobre los coeficientes de nivel y
  las tablas de Pesaran-Shin-Smith (valores críticos por k y caso, y superficies de p-valor en
  log(estadístico), generadas en `PssCriticalValues.cs` desde
  `statsmodels.tsa.ardl.pss_critical_values`). Los multiplicadores de largo plazo usan el método
  delta con la covarianza clásica del ARDL y la velocidad de ajuste es el coeficiente `y.L1` del
  UECM.
- `panel` reproduce `linearmodels.panel` en el caso del vector (panel balanceado, un regresor,
  efectos fijos por individuo): `PooledOLS` (MCO con constante), `PanelOLS` (transformación within,
  `df_resid = N − k − G`, prueba F de poolabilidad) y `RandomEffects` (componentes de varianza de
  Swamy-Arora con `σ²_e = SSR_within/(N − k − G + 1)` y `σ²_u = SSR_entre/(G − k) − σ²_e/T̄`,
  cuasi-demean con θ por individuo y MCO). Las covarianzas agrupadas replican el estimador de
  conglomerados de linearmodels (`scale = n/(n − efectos − k)` y, cuando el estimador es
  `clustered` y los efectos están anidados en los conglomerados, `efectos = 0`, como decide
  `auto_df`), además de las variantes robusta, clásica y de Driscoll-Kraay. Se reproducen los tres
  R² (dentro/entre/total, con la variante sin centrar de los modelos sin constante), la prueba F de
  poolabilidad (`gl₁ = G − 1` en el modelo sin constante), el Breusch-Pagan LM insesgado, el
  Hausman con covarianzas clásicas (no calculable cuando la diferencia no es definida positiva) y
  el dataset/gráfico de efectos fijos por individuo.

## 8. Pendiente de paridad: `garch`

`vecm` ya está implementado y registrado con paridad (ver §7). Sólo `garch` queda bloqueado: su vector es el punto donde se detuvo SLSQP, no el máximo de verosimilitud.

1. `garch`: **no se registró**; `GarchAnalyses.cs` implementa GARCH(p,q), ARCH(q), GJR y TARCH
   con distribuciones normal y t de Student (EGARCH, APARCH, t asimétrica y GED devuelven
   `NOT_SUPPORTED`), MLE, persistencia, diagnósticos, VaR/Kupiec y pronóstico. El vector usa
   GARCH(1,1), media constante y t; su punto es el que devolvió SLSQP de SciPy (`arch` 8.0.0,
   `options={"maxiter": 500}`, `ftol=1e-6` por defecto), no el máximo de verosimilitud:
   - `optimization_result` del ajuste de Sigma: `success=True`, `status=0`, `nit=16`,
     `nfev=100`, `njev=16` y `jac = [3.0518e-5, 1.2207e-4, −9.7656e-3, −8.9111e-3, 4.5776e-5]`.
   - Con el motor C# (MLE por BFGS + Newton, con el backcast y los límites de varianza fijos
     como en `arch`) el óptimo es `μ = 0.5026875`, `ω = 268.0205750`, `α = 0.1608397`,
     `β = 0.8259749`, `ν = 16.1475158` y `llf = −1449.9566882`; el vector guarda
     `μ = 0.4796324`, `ω = 274.3818453`, `α = 0.1613203`, `β = 0.8249340`, `ν = 16.1785905`
     y `llf = −1449.9570562`. La mejora de verosimilitud es 3.68e-4 y los desplazamientos son
     `Δμ = −0.023055`, `Δω = +6.36127`, `Δα = −4.8056e-4`, `Δβ = +1.0409e-3`,
     `Δν = −0.0310748`.
   - Eso excede la tolerancia de `keyResults` (1e-5 relativo) por factores 10²–10⁴:
     μ 4.8e-2, ω 2.3e-2, α 3.0e-3, β 1.3e-3, ν 1.9e-3. La persistencia es 0.9868146 frente a
     0.9862543 (`Δ = 5.60e-4`; la tile formateada a 4 decimales ya falla la tolerancia `2e-4`),
     la vida media 52.2221 frente a 50.0790 (`Δ = 2.1431`), el VaR da 12 violaciones frente a
     13 y el p-valor de Kupiec 1 frente a 0.769974.
   - En los datasets (tolerancia 1e-6) el máximo error por celda es: volatilidad 0.4661
     (observación 82, 0.31 %), residuo estandarizado 6.5551e-3, VaR 0.7335 y pronóstico de
     varianza 30.64 en el horizonte 5 (0.56 %). Sólo `rendimiento` coincide exacto (es la
     entrada); AIC y BIC difieren 7.36e-4, así que pasan la tabla pero no los `keyResults`.
   - No sirve afinar el óptimo: con `ftol=1e-14` SLSQP aún se detiene en
     `llf = −1449.9566931` (gradiente 1.5e-3) y Nelder-Mead/Newton llega a `−1449.9566882`.
     El punto del vector es una parada de SLSQP, no un óptimo. Reproducirlo exigiría emular
     bit a bit el SLSQP de SciPy (actualizaciones BFGS, búsqueda lineal y gradientes por
     diferencias finitas) y el orden de operaciones de `arch`, igual que `markov`/`ucm` (§6).
   - Por eso no se registra en `EconometricsAnalyses.All`: los errores de validación y el
     catálogo sí coinciden (probados registrando el análisis temporalmente), pero el ejemplo
     no. Para desbloquearlo hay que regenerar el vector con un óptimo convergente (por ejemplo
     `fit(..., tol=1e-10)` seguido de un pulido, o un vector generado con `minimize` de
     tolerancia estricta) y volver a medir.
3. `regression-mixed`: el vector es el punto donde se detuvo L-BFGS-B de statsmodels, no el
   óptimo del modelo. `_mixed` ajusta `smf.mixedlm(...).fit(reml=True, method="lbfgs")`; la
   superficie REML (verosimilitud perfilada sobre β y σ²) alcanza su máximo en la razón
   var_grupo/σ² = 0.32448723, pero statsmodels devuelve 0.32448792 (2.1e-6 por encima) porque
   el criterio de parada de L-BFGS-B (`factr·ε = 2.22e-9` sobre la mejora relativa) se cumple
   antes de converger. Comprobado con el propio `model.loglike` de statsmodels: su máximo está
   en `s = 0.5696378235` y el resultado devuelto en `s = 0.5696384160`.
   Con el óptimo exacto (implementado en `MixedModelAnalyses` con verosimilitud perfilada y
   refinamiento de Brent) coinciden título, subtítulo, las cinco tablas (tolerancia 2e-4), los
   `keyResults` `n`, `groups`, `icc`, `var_group`, `resid_var` y `coefficients` (1e-5) y la
   columna `ajustado` del dataset; fallan 5 de las 240 celdas de `residuo`, con diferencias de
   1.05e-6 a 1.19e-6 frente a la tolerancia de 1e-6, porque el BLUP se desplaza ~1e-6 al mover
   la varianza 2e-6. Reproducirlo exige emular bit a bit el L-BFGS-B de SciPy (la trayectoria
   registrada son 10 evaluaciones cuyos pasos dependen de la búsqueda lineal), igual que
   `markov`/`ucm` (§6). No se registra; `MixedModelAnalyses.cs` queda completo y listo para
   cuando el vector se regenere con un óptimo convergente.
4. `regression-gam`: **sí es reproducible y se registró** (`GamAnalyses`). Aunque el estilo de
   llamada es `GLMGam(y, X, smoother=BSplines(X, df, degree)).fit()`, el constructor usa
   `alpha = 0` por defecto y la familia es gaussiana, así que no hay penalización ni búsqueda
   GCV: el ajuste es la proyección de `y` sobre `[X, base de B-splines]` y PIRLS converge en un
   paso. Se replicaron los nudos de patsy (frontera repetida `grado + 1` veces más el cuantil
   0.5 con interpolación lineal), la base por el algoritmo de de Boor y el rango del diseño
   (rank-deficiente: cada predictor vive en el espacio de sus splines) con una QR con pivoteo.
   El AIC usa el rango (`-2·llf + 2·rango`) y la escala ML (`rss/n`). Verificado contra
   statsmodels: base y diseño a 1.1e-16, valores ajustados a 3.0e-14 y AIC/R² exactos.
