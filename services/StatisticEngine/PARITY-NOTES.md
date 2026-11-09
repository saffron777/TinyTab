# Notas de paridad — StatisticEngine vs. motor TypeScript de TinyTab

Este documento fija **qué significa "paridad"** para este microservicio, qué defectos del motor
original se reproducen a propósito y qué desviaciones existen y por qué.

## 1. Método

1. `tools/golden/` ejecuta el motor TypeScript real (Vitest + alias `@domain`) y escribe
   vectores JSON con entradas y salidas: **16 módulos**, **21 distribuciones**, **60 análisis**,
   catálogo completo y formato de texto de sesión.
2. `StatisticEngine.Domain.Tests` recorre **todos** los casos de cada vector y compara contra la
   implementación C#. Un vector con una función sin implementación registrada hace fallar la suite.
3. Reglas de comparación:
   - Textos, claves, estructura y **mensajes de error**: exactos.
   - Estado entero del RNG (`nextUint32`, `next`, `binomial`, `poisson`): **bit a bit**.
   - Dobles analíticos, cuantiles y distribuciones: `|a-b| ≤ 1e-12 · max(1, |a|, |b|)`.
   - Ajustes iterativos (Weibull MLE, IRLS, k-means, AR): `1e-9`.
   - Formato de números (`fmt`, `fmtP`, `toFixed`, `toExponential`, `String(number)`):
     reimplementación exacta de ECMAScript y corpus de cadenas comparado carácter a carácter.

Regenerar vectores (no modifica el código de TinyTab):

```powershell
npx vitest run --config services/StatisticEngine/tools/golden/vitest.config.ts
```

## 2. Comportamientos del motor reproducidos a propósito

Se portaron **sin corregir**; cada uno tiene su caso en los vectores y un comentario `// paridad:`
en el código C#.

| # | Comportamiento | Origen TS |
| --- | --- | --- |
| 1 | Gage R&R publica como columna `p` el **percentil 95 de F**, no un p-valor | `msa.ts:29-40,162-164` |
| 2 | `analyzeFactorial` indexa la matriz X por `standardOrder-1` con corridas aleatorizadas (desalineación X↔y) | `doe.ts:175,180-183` |
| 3 | Capacidad: `sigmaWithin == overallSd` ⇒ `cp == pp`, `cpk == ppk`; `cpk = NaN` con un solo límite | `spc.ts:264-298` |
| 4 | EWMA: ancho de límites calculado con el factor de varianza del primer punto | `spc.ts:217` |
| 5 | CUSUM: límites fijos `[0, 5]`, sin parámetro `h` | `spc.ts:236` |
| 6 | ANOVA de dos factores aditivo (sin interacción), admite celdas desbalanceadas | `anova.ts:81-131` |
| 7 | `fitAr`: errores estándar heurísticos `sqrt(var·(1 + 0.1h))` | `timeseries.ts:228` |
| 8 | `bestSubsets`: tope de 20 modelos y `NOT_SUPPORTED` con más de 14 predictores | `regression.ts:152-177` |
| 9 | `poissonRegression`: `NON_CONVERGENCE` estricto a 100 iteraciones (logística 50) | `regression.ts:397` |
| 10 | Búsquedas de tamaño de muestra devuelven el tope del bucle sin error si no alcanzan la potencia | `power.ts:28,44,64` |
| 11 | Winters no valida α/β/γ; SES no valida longitud de la serie | `timeseries.ts:68-131` |
| 12 | `processCapability` publica `lowerSpec`/`upperSpec = NaN` cuando el límite falta | `spc.ts:291-292` |
| 13 | PACF: el algoritmo de Durbin-Levinson lee `phi[lag-1]` fuera de rango ⇒ `NaN` desde el lag 2 | `timeseries.ts` |
| 14 | `decompose`: si la tendencia es 0/NaN usa la media de la serie como sustituto | `timeseries.ts:175` |
| 15 | `twoSampleT` y `oneSampleT` repiten una tabla «Estimación» **sin filas** | `registry-basic.ts` |
| 16 | El análisis de distribución omitido usa `x = 1` y `p = 0.95` por defecto | `registry-basic.ts` |

## 3. Desviaciones justificadas

### 3.1 `varimax` y `factorAnalysis`: invariantes en lugar de igualdad exacta

La rotación varimax del motor **no converge**: usa `b = (a/h)³` en vez de `a³/h`, de modo que el
ángulo no decae y las 100 iteraciones se consumen con rotaciones de ~0.3 rad (comprobado
ejecutando el algoritmo original en Node). El resultado final depende de la trayectoria exacta de
`Math.pow/atan2/cos/sin` de V8:

- perturbar **un bit** de la matriz de entrada mueve el resultado `7.4e-5` en una matriz 4×2 y
  **1.28** en una 3×2;
- por tanto es imposible reproducirlo bit a bit desde .NET (y probablemente cambie entre versiones
  de V8).

Decisión: la implementación C# reproduce el **algoritmo** (misma fórmula, mismos cortes, mismas
100 iteraciones) y la suite verifica **invariantes de una rotación ortogonal**: misma forma,
mismas sumas de cuadrados por fila, mismo total y mismos productos escalares entre filas. Para
`factorAnalysis` se comparan exactos `labels`, `eigenvalues` y `communalities` (invariantes bajo
rotación), y en el análisis `multivariate-factor` las cargas se admiten con diferencia absoluta
≤ 0.01.

### 3.2 Tolerancias numéricas

- `1e-12` relativa en funciones analíticas: `Math.Exp/Log/Pow/Sin/Cos/Atan2` de V8 y .NET pueden
  diferir 1 ulp.
- `1e-9` en ajustes iterativos y en los gráficos de los 60 análisis (Weibull MLE, AR).
- RNG entero: exacto (aritmética `uint` sin signo, reproduce `Math.imul` y los desplazamientos).

### 3.3 Serialización JSON

- Los no finitos se emiten como `"NaN"`, `"Infinity"` y `"-Infinity"`
  (`JsonNumberHandling.AllowNamedFloatingPointLiterals`), porque JSON no puede representarlos.
- Las propiedades nulas/ausentes se omiten (`WhenWritingNull`), igual que `undefined` en el motor.
- Los **vectores dorados** codifican además `-0` como `{"$num":"-0"}` para no perder el signo.

### 3.4 Contrato del API

- `numeric` acepta sólo números y `text` sólo cadenas: un `null` se rechaza con 400 en lugar de
  intentar imitar el manejo de faltantes de la hoja de cálculo (fuera del motor).
- `params` se aceptan como número, cadena o booleano y se convierten con la misma semántica que
  `pnum`/`pstr`/`pbool` (incluida la cadena vacía como «ausente»).
- En `capability`, un límite ausente se modela como `null`, **nunca** como `double.NaN`: con
  `NaN`, .NET lo trataría como límite presente y contaminaría `ppBelow`/`ppAbove` y `Cp`.
- `?format=text` reproduce exactamente `formatAnalysisResult` (incluido `─`, el ancho de columnas
  y el redondeo de `fmt`).

### 3.5 Supresiones de analizadores

`CA1859` (tipos concretos) y, sólo en pruebas, `xUnit1051` (tokens de cancelación) y `CA1861`
(arrays constantes) están desactivados en `.editorconfig`: son sugerencias de estilo que no
aportan al objetivo de paridad y ensuciaban el build con `TreatWarningsAsErrors`.

## 4. Estado de la verificación

| Suite | Resultado |
| --- | --- |
| Dominio (paridad + unitarias) | **29/29** |
| API (contrato HTTP) | **19/19** |
| Cobertura funcional | 16/16 módulos, 21/21 distribuciones, 60/60 análisis |

Los vectores cubren todas las funciones exportadas del dominio salvo las que reciben delegados
(`bisectInverse`, cubierta indirectamente por las inversas de distribución) y el generador de
nombres de la hoja de trabajo (fuera de alcance).
