# API de StatisticEngine — guía completa con ejemplos

Motor estadístico de TinyTab expuesto como microservicio HTTP: **21 distribuciones** y
**60 análisis** con paridad numérica verificada. Esta guía enumera **todos** los endpoints y
muestra una petición real por cada análisis y cada distribución.

> Documento generado con `tools/docs/generate-api-docs.ps1` consultando el propio servicio en
> ejecución: las columnas, parámetros, estados y respuestas son los que devuelve el motor.

## 1. Arranque y URLs

```powershell
cd services/StatisticEngine
dotnet run --project src/StatisticEngine.Api --launch-profile "StatisticEngine.Api (HTTP)"
```

| Recurso | URL por defecto |
| --- | --- |
| Página de inicio (enlaces) | `http://localhost:5080/` |
| Documentación interactiva (Scalar) | `http://localhost:5080/scalar/v1` |
| Contrato OpenAPI | `http://localhost:5080/openapi/v1.json` |
| Inventario de rutas | `http://localhost:5080/api/v1/endpoints` |
| API | `http://localhost:5080/api/v1` |

Con el perfil HTTPS de Visual Studio los mismos recursos viven en `https://localhost:54236`.
La URL no está fijada en `appsettings.json`: manda el perfil, `--urls` o `ASPNETCORE_URLS`.

Los ejemplos usan `curl.exe` con comillas simples para que funcionen igual en PowerShell y en
bash. En PowerShell también sirve `Invoke-RestMethod`:

```powershell
$body = '{"numeric":{"x":[1,2,3,4,5]}}'
Invoke-RestMethod -Uri "http://localhost:5080/api/v1/analyses/describe/run" `
  -Method Post -ContentType "application/json" -Body $body
```

## 2. Convenciones

- **JSON camelCase**; las propiedades nulas se omiten.
- **Números no finitos** se serializan como `"NaN"`, `"Infinity"` y `"-Infinity"`.
- **Errores**: `application/problem+json` con `status`, `title`, `code` (código del motor),
  `traceId` y `analysisId` cuando aplica.
- Cabecera `X-StatisticEngine-Version` en todas las respuestas.
- `numeric` acepta solo números y `text` solo cadenas; `params` acepta número, cadena o booleano
  (la cadena vacía significa «parámetro ausente»).
- En los análisis con **columnas múltiples** las claves de `numeric`/`text` son nombres libres de
  columna, y el **orden de inserción importa** (p. ej. en regresión la primera columna es la
  respuesta).
- El **muestreo es determinista**: la misma semilla produce la misma serie.

## 3. Mapa de endpoints

| Método | Ruta | Descripción |
| --- | --- | --- |
| `GET` | `/` | Página de inicio con enlaces (HTML) |
| `GET` | `/api/v1/analyses` | Catálogo de análisis disponibles |
| `GET` | `/api/v1/analyses/{id}` | Definición de un análisis: columnas, parámetros y opciones |
| `POST` | `/api/v1/analyses/{id}/run` | Ejecuta un análisis del catálogo |
| `POST` | `/api/v1/analyses/{id}/validate` | Valida la entrada sin ejecutar el análisis |
| `GET` | `/api/v1/distributions` | Catálogo de distribuciones con sus parámetros |
| `GET` | `/api/v1/distributions/{id}` | Metadatos de una distribución |
| `POST` | `/api/v1/distributions/{id}/evaluate` | PDF/PMF, CDF, inversa y momentos para listas de x y de probabilidades |
| `POST` | `/api/v1/distributions/{id}/sample` | Muestreo determinista con semilla |
| `GET` | `/api/v1/endpoints` | Inventario de rutas publicadas por el servicio |
| `GET` | `/api/v1/meta` | Versión, límites efectivos y tiempo en marcha |
| `GET` | `/health/live` | Liveness del proceso |
| `GET` | `/health/ready` | Readiness con autotest numérico |
| `GET` | `/openapi/{documentName}.json` | Contrato OpenAPI |
| `GET` | `/scalar/{documentName?}` | Documentación interactiva (Scalar) |

## 4. Flujo típico

1. `GET /api/v1/analyses` para descubrir los análisis y sus entradas.
2. `POST /api/v1/analyses/{id}/validate` para comprobar la entrada sin calcular.
3. `POST /api/v1/analyses/{id}/run` para ejecutar (o `?format=text` para el texto de sesión).
4. `GET /api/v1/analyses/{id}` para leer la definición exacta (columnas, parámetros, opciones).

```powershell
# 1) qué necesita "capability"
curl.exe "http://localhost:5080/api/v1/analyses/capability"

# 2) validar sin ejecutar
curl.exe -X POST "http://localhost:5080/api/v1/analyses/capability/validate" `
  -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2]},"params":{"lsl":6,"usl":14}}'

# 3) ejecutar
curl.exe -X POST "http://localhost:5080/api/v1/analyses/capability/run" `
  -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10,9.6,10.9,9.3,10.7,10.2],"params":{"lsl":6,"usl":14}}'

# 4) mismo cálculo, texto de sesión
curl.exe -X POST "http://localhost:5080/api/v1/analyses/capability/run?format=text" `
  -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2]},"params":{"lsl":6,"usl":14}}'
```

## 5. Catálogo de análisis por categoría

### Estadísticas básicas (5)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `describe` | Estadísticas descriptivas | `x` (num, múltiple) · `confidence` (number) |
| `graphical-summary` | Resumen gráfico | `x` (num) · `confidence` (number) |
| `correlation` | Correlación | `x` (num, múltiple) |
| `covariance` | Covarianza | `x` (num, múltiple) |
| `normality` | Prueba de normalidad | `x` (num) |

### Distribuciones (1)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `probability-distribution` | Distribución de probabilidad | `distribution` (select) · `x` (number) · `p` (number) |

### Tablas (3)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `tally` | Tally individual variables | `x` (num) · `t` (texto) |
| `cross-tab` | Tabla cruzada y chi-cuadrado | `row` (texto) · `column` (texto) |
| `chi-square-gof` | Chi-cuadrado de bondad de ajuste | `counts` (num) · `labels` (texto) |

### Pruebas de hipótesis (9)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `one-sample-t` | t de 1 muestra | `x` (num) · `mu` (number) · `confidence` (number) · `alternative` (select) |
| `one-sample-z` | Z de 1 muestra | `x` (num) · `sigma` (number) · `mu` (number) |
| `two-sample-t` | t de 2 muestras | `x1` (num) · `x2` (num) · `pooled` (select) |
| `paired-t` | t pareada | `x1` (num) · `x2` (num) |
| `one-proportion` | Proporción de 1 muestra | `successes` (integer) · `trials` (integer) · `p0` (number) |
| `two-proportions` | Proporción de 2 muestras | `x1` (integer) · `n1` (integer) · `x2` (integer) · `n2` (integer) |
| `one-variance` | Varianza de 1 muestra | `x` (num) · `sigma0` (number) |
| `two-variances` | Varianza de 2 muestras | `x1` (num) · `x2` (num) |
| `equivalence-tost` | Prueba de equivalencia (TOST) | `x1` (num) · `x2` (num) · `margin` (number) |

### No paramétricos (6)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `nonparam-sign` | Prueba de los signos | `x` (num) · `median` (number) |
| `nonparam-wilcoxon` | Wilcoxon de rangos con signo | `x` (num) · `median` (number) |
| `nonparam-mann-whitney` | Mann-Whitney | `x1` (num) · `x2` (num) |
| `nonparam-kruskal-wallis` | Kruskal-Wallis | `y` (num) · `factor` (texto) |
| `nonparam-runs` | Prueba de rachas | `x` (num) |
| `nonparam-spearman` | Correlación de Spearman | `x` (num) · `y` (num) |

### ANOVA (3)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `anova-one-way` | ANOVA de un factor | `y` (num) · `factor` (texto) · `posthoc` (select) |
| `anova-two-way` | ANOVA de dos factores | `y` (num) · `a` (texto) · `b` (texto) |
| `equal-variances` | Prueba de igualdad de varianzas | `y` (num) · `factor` (texto) |

### Regresión (5)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `regression-multiple` | Regresión múltiple | `y` (num) · `x` (num, múltiple) |
| `regression-stepwise` | Regresión stepwise | `y` (num) · `x` (num, múltiple) · `direction` (select) |
| `regression-best-subsets` | Mejores subconjuntos | `y` (num) · `x` (num, múltiple) |
| `regression-logistic` | Regresión logística binaria | `y` (num) · `x` (num, múltiple) |
| `regression-poisson` | Regresión de Poisson | `y` (num) · `x` (num, múltiple) |

### Control de calidad (7)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `chart-xbar-r` | Carta X̄-R | `x` (num) · `size` (integer) |
| `chart-imr` | Carta I-MR | `x` (num) |
| `chart-p` | Carta P | `defective` (num) · `sample` (num) |
| `chart-c` | Carta C | `defects` (num) |
| `chart-u` | Carta U | `defects` (num) · `units` (num) |
| `chart-ewma` | Carta EWMA | `x` (num) · `lambda` (number) |
| `chart-cusum` | Carta CUSUM | `x` (num) · `k` (number) |

### Capacidad (1)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `capability` | Análisis de capacidad | `x` (num) · `lsl` (number) · `usl` (number) · `method` (select) |

### Sistemas de medida (1)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `gage-rr` | Gage R&R (cruzado) | `value` (num) · `part` (texto) · `operator` (texto) · `trial` (number) |

### DOE (1)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `doe-analyze` | Analizar diseño factorial (efectos) | `y` (num) · `x` (num, múltiple) |

### Series temporales (8)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `timeseries-acf` | Función de autocorrelación (ACF) | `x` (num) · `lags` (integer) |
| `timeseries-pacf` | Autocorrelación parcial (PACF) | `x` (num) · `lags` (integer) |
| `timeseries-moving-average` | Media móvil | `x` (num) · `window` (integer) |
| `timeseries-ses` | Suavizado exponencial simple | `x` (num) · `alpha` (number) |
| `timeseries-des` | Suavizado exponencial doble | `x` (num) · `alpha` (number) · `beta` (number) |
| `timeseries-winters` | Suavizado de Winters | `x` (num) · `period` (integer) · `alpha` (number) · `beta` (number) · `gamma` (number) |
| `timeseries-decompose` | Descomposición estacional | `x` (num) · `period` (integer) |
| `timeseries-ar` | Modelo AR y pronóstico | `x` (num) · `order` (integer) · `horizon` (integer) |

### Fiabilidad (2)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `reliability-weibull` | Ajuste de distribución Weibull | `x` (num) |
| `reliability-kaplan-meier` | Kaplan-Meier | `time` (num) · `event` (number) |

### Multivariante (4)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `multivariate-pca` | Análisis de componentes principales | `x` (num, múltiple) |
| `multivariate-factor` | Análisis factorial | `x` (num, múltiple) · `factors` (integer) |
| `multivariate-kmeans` | Análisis de conglomerados K-means | `x` (num, múltiple) · `k` (integer) |
| `multivariate-lda` | Análisis discriminante (2 grupos) | `x` (num, múltiple) · `group` (texto) |

### Potencia y tamaño de muestra (4)

| `id` | Nombre | Entradas |
| --- | --- | --- |
| `power-t1` | Potencia y tamaño de muestra (t de 1 muestra) | `effect` (number) · `n` (integer) · `power` (number) · `alpha` (number) |
| `power-prop1` | Potencia y tamaño de muestra (proporción) | `p0` (number) · `p1` (number) · `n` (integer) · `power` (number) |
| `power-anova1` | Potencia y tamaño de muestra (ANOVA de 1 factor) | `effect` (number) · `groups` (integer) · `n` (integer) |
| `power-t2` | Tamaño de muestra (t de 2 muestras) | `effect` (number) · `power` (number) |

## 6. Los 60 análisis, uno por uno

Cada apartado incluye la definición exacta (extraída del catálogo del servicio) y una petición
real con su resultado observado.

### Estadísticas básicas

#### `Estadísticas descriptivas` — `describe`

Media, desviación estándar, cuartiles, intervalo de confianza y pruebas de normalidad.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |
| `confidence` | number · por defecto `0.95` | sí | Nivel de confianza |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/describe/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]},"params":{"confidence":0.95}}'
```

Resultado observado: **200** · «Resumen» · 4 filas · 1 gráfico

#### `Resumen gráfico` — `graphical-summary`

Resumen descriptivo con histograma, prueba de normalidad e intervalo de confianza.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Variable |
| `confidence` | number · por defecto `0.95` | sí | Nivel de confianza |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/graphical-summary/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"confidence":0.95}}'
```

Resultado observado: **200** · «Estadísticas» y 2 secciones más · 1 fila · 2 gráficos

#### `Correlación` — `correlation`

Matriz de correlaciones de Pearson entre dos o más variables.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/correlation/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]}}'
```

Resultado observado: **200** · «Matriz de correlaciones» · 4 filas · 1 gráfico

#### `Covarianza` — `covariance`

Matriz de covarianzas entre dos o más variables.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/covariance/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]}}'
```

Resultado observado: **200** · «Matriz de covarianzas» · 4 filas · 1 gráfico

#### `Prueba de normalidad` — `normality`

Prueba de Anderson-Darling para la normalidad de los datos.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Variable |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/normality/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]}}'
```

Resultado observado: **200** · «Anderson-Darling» · 1 fila · 1 gráfico

### Distribuciones

#### `Distribución de probabilidad` — `probability-distribution`

Funciones de densidad, acumulada e inversa para 21 distribuciones.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `distribution` | select (normal|t|chi2|f|gamma|beta|exponential|weibull|lognormal|uniform|logistic|cauchy|laplace|triangular|smallestExtreme|largestExtreme|foldedNormal|binomial|poisson|hypergeometric|negativeBinomial) · por defecto `normal` | sí | Distribución |
| `x` | number · por defecto `1` | sí | Valor de x |
| `p` | number · por defecto `0.95` | sí | Probabilidad acumulada |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/probability-distribution/run" -H "Content-Type: application/json" --data-raw '{"params":{"distribution":"normal","x":1,"p":0.95}}'
```

Resultado observado: **200** · «Parámetros» y 1 sección más · 2 filas · 1 gráfico

### Tablas

#### `Tally individual variables` — `tally`

Conteo, porcentaje y porcentaje acumulado por categoría.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | no | Variable numérica (opcional) |
| `t` | texto | no | Variable de texto (opcional) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/tally/run" -H "Content-Type: application/json" --data-raw '{"text":{"t":["B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A"]}}'
```

Resultado observado: **200** · «Conteo» · 3 filas · 1 gráfico

#### `Tabla cruzada y chi-cuadrado` — `cross-tab`

Tabla de contingencia con prueba de independencia chi-cuadrado.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `row` | texto | sí | Variable de fila |
| `column` | texto | sí | Variable de columna |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/cross-tab/run" -H "Content-Type: application/json" --data-raw '{"text":{"row":["B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A"],"column":["Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana"]}}'
```

Resultado observado: **200** · «Conteos» y 1 sección más · 4 filas · 1 gráfico

#### `Chi-cuadrado de bondad de ajuste` — `chi-square-gof`

Compara frecuencias observadas con proporciones esperadas iguales.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `counts` | numérico | sí | Frecuencias observadas |
| `labels` | texto | no | Etiquetas (opcional) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chi-square-gof/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"counts":[12,18,9,15]}}'
```

Resultado observado: **200** · «Contribuciones» y 1 sección más · 4 filas · 1 gráfico

### Pruebas de hipótesis

#### `t de 1 muestra` — `one-sample-t`

Contraste de la media de una población contra un valor de referencia.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Muestra |
| `mu` | number · por defecto `0` | sí | Media hipotética (H₀) |
| `confidence` | number · por defecto `0.95` | sí | Nivel de confianza |
| `alternative` | select (two-sided|less|greater) · por defecto `two-sided` | sí | Hipótesis alternativa |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/one-sample-t/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"mu":0,"confidence":0.95,"alternative":"two-sided"}}'
```

Resultado observado: **200** · «Estimación» y 3 secciones más · 0 filas · 1 gráfico

#### `Z de 1 muestra` — `one-sample-z`

Contraste de la media con σ conocida.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Muestra |
| `sigma` | number · por defecto `1` | sí | σ conocida |
| `mu` | number · por defecto `0` | sí | Media hipotética |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/one-sample-z/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"sigma":1,"mu":0}}'
```

Resultado observado: **200** · «Prueba» y 1 sección más · 1 fila · 1 gráfico

#### `t de 2 muestras` — `two-sample-t`

Comparación de medias de dos muestras independientes (Welch o varianzas agrupadas).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x1` | numérico | sí | Muestra 1 |
| `x2` | numérico | sí | Muestra 2 |
| `pooled` | select (false|true) · por defecto `false` | sí | Asumir varianzas iguales |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/two-sample-t/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05]},"params":{"pooled":"false"}}'
```

Resultado observado: **200** · «Prueba» y 1 sección más · 1 fila · 1 gráfico

#### `t pareada` — `paired-t`

Contraste de medias en observaciones pareadas.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x1` | numérico | sí | Muestra 1 |
| `x2` | numérico | sí | Muestra 2 |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/paired-t/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05]}}'
```

Resultado observado: **200** · «Diferencia» y 1 sección más · 1 fila · 1 gráfico

#### `Proporción de 1 muestra` — `one-proportion`

Contraste de una proporción con aproximación normal e intervalo de Wilson.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `successes` | integer · por defecto `10` | sí | Éxitos |
| `trials` | integer · por defecto `40` | sí | Ensayos |
| `p0` | number · por defecto `0.5` | sí | Proporción nula |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/one-proportion/run" -H "Content-Type: application/json" --data-raw '{"params":{"successes":10,"trials":40,"p0":0.5}}'
```

Resultado observado: **200** · «Estimación» y 1 sección más · 1 fila · 1 gráfico

#### `Proporción de 2 muestras` — `two-proportions`

Comparación de dos proporciones independientes.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x1` | integer · por defecto `12` | sí | Éxitos muestra 1 |
| `n1` | integer · por defecto `50` | sí | Ensayos muestra 1 |
| `x2` | integer · por defecto `18` | sí | Éxitos muestra 2 |
| `n2` | integer · por defecto `50` | sí | Ensayos muestra 2 |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/two-proportions/run" -H "Content-Type: application/json" --data-raw '{"params":{"x1":12,"n1":50,"x2":18,"n2":50}}'
```

Resultado observado: **200** · «Diferencia» y 1 sección más · 1 fila · 1 gráfico

#### `Varianza de 1 muestra` — `one-variance`

Contraste de una varianza contra un valor de referencia.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Muestra |
| `sigma0` | number · por defecto `1` | sí | Desviación hipotética (σ₀) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/one-variance/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"sigma0":1}}'
```

Resultado observado: **200** · «Prueba» y 1 sección más · 1 fila · 1 gráfico

#### `Varianza de 2 muestras` — `two-variances`

Comparación de dos varianzas mediante la prueba F.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x1` | numérico | sí | Muestra 1 |
| `x2` | numérico | sí | Muestra 2 |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/two-variances/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05]}}'
```

Resultado observado: **200** · «Prueba F» · 1 fila · 1 gráfico

#### `Prueba de equivalencia (TOST)` — `equivalence-tost`

Dos pruebas t de una cola para demostrar equivalencia con un margen.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x1` | numérico | sí | Muestra 1 |
| `x2` | numérico | sí | Muestra 2 |
| `margin` | number · por defecto `1` | sí | Margen de equivalencia |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/equivalence-tost/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05]},"params":{"margin":1}}'
```

Resultado observado: **200** · «Resultados» · 1 fila · 1 gráfico

### No paramétricos

#### `Prueba de los signos` — `nonparam-sign`

Contraste de la mediana mediante la distribución binomial.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Muestra |
| `median` | number · por defecto `0` | sí | Mediana nula |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/nonparam-sign/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"median":0}}'
```

Resultado observado: **200** · «Resultados» · 1 fila · 1 gráfico

#### `Wilcoxon de rangos con signo` — `nonparam-wilcoxon`

Prueba no paramétrica para muestras pareadas o una muestra.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Muestra |
| `median` | number · por defecto `0` | sí | Mediana nula |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/nonparam-wilcoxon/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"median":0}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

#### `Mann-Whitney` — `nonparam-mann-whitney`

Comparación de dos muestras independientes por rangos.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x1` | numérico | sí | Muestra 1 |
| `x2` | numérico | sí | Muestra 2 |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/nonparam-mann-whitney/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05]}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

#### `Kruskal-Wallis` — `nonparam-kruskal-wallis`

Comparación de k muestras independientes por rangos.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `factor` | texto | sí | Factor |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/nonparam-kruskal-wallis/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"text":{"factor":["B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A"]}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

#### `Prueba de rachas` — `nonparam-runs`

Aleatoriedad de una secuencia alrededor de la mediana.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Secuencia |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/nonparam-runs/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

#### `Correlación de Spearman` — `nonparam-spearman`

Correlación de rangos entre dos variables.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | X |
| `y` | numérico | sí | Y |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/nonparam-spearman/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

### ANOVA

#### `ANOVA de un factor` — `anova-one-way`

Comparación de medias entre niveles de un factor, con comparaciones múltiples.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `factor` | texto | sí | Factor |
| `posthoc` | select (true|false) · por defecto `true` | sí | Comparaciones por pares (Holm) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/anova-one-way/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"text":{"factor":["B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A"]},"params":{"posthoc":"true"}}'
```

Resultado observado: **200** · «Estadísticas por grupo» y 3 secciones más · 3 filas · 1 gráfico

#### `ANOVA de dos factores` — `anova-two-way`

Modelo aditivo de dos factores (sin interacción).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `a` | texto | sí | Factor A |
| `b` | texto | sí | Factor B |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/anova-two-way/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"text":{"a":["B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A"],"b":["Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana","Tarde","Noche","Mañana"]}}'
```

Resultado observado: **200** · «Tabla ANOVA» y 1 sección más · 4 filas · 1 gráfico

#### `Prueba de igualdad de varianzas` — `equal-variances`

Pruebas de Levene y Bartlett sobre los grupos de un factor.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `factor` | texto | sí | Factor |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/equal-variances/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"text":{"factor":["B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A","B","C","A"]}}'
```

Resultado observado: **200** · «Levene» y 1 sección más · 1 fila · 1 gráfico

### Regresión

#### `Regresión múltiple` — `regression-multiple`

Ajuste por mínimos cuadrados (QR) con diagnóstico de residuos y VIF.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `x` | numérico (una o varias columnas) | sí | Predictores |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/regression-multiple/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]}}'
```

Resultado observado: **200** · «Coeficientes» y 1 sección más · 5 filas · 1 gráfico

#### `Regresión stepwise` — `regression-stepwise`

Selección de predictores por AIC (forward/backward).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `x` | numérico (una o varias columnas) | sí | Predictores |
| `direction` | select (forward|backward) · por defecto `forward` | sí | Dirección |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/regression-stepwise/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[34.648,32.654,36.491,32.312,35.25,33.696,35.559,34.093,34.307,34.852,33.449,35.93,33.769,34.885,34.026,32.972,35.633,35.52,33.478,34.681,33.917,36.071,34.36,33.208,35.063,36.408,32.466,34.682,33.84,33.985],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]},"params":{"direction":"forward"}}'
```

Resultado observado: **200** · «Coeficientes» y 1 sección más · 2 filas · 1 gráfico

#### `Mejores subconjuntos` — `regression-best-subsets`

Modelos con mayor R² ajustado hasta 4 predictores.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `x` | numérico (una o varias columnas) | sí | Predictores |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/regression-best-subsets/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]}}'
```

Resultado observado: **200** · «Modelos (ordenados por R² ajustado)» · 15 filas · 1 gráfico

#### `Regresión logística binaria` — `regression-logistic`

Modelo logit ajustado por IRLS con desviación y AIC.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta 0/1 |
| `x` | numérico (una o varias columnas) | sí | Predictores |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/regression-logistic/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62]}}'
```

Resultado observado: **200** · «Coeficientes» y 1 sección más · 3 filas · 1 gráfico

#### `Regresión de Poisson` — `regression-poisson`

Modelo log-lineal para respuestas de conteo (IRLS).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Conteo |
| `x` | numérico (una o varias columnas) | sí | Predictores |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/regression-poisson/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[5.0,5.0,3.0,1.0,1.0,2.0,4.0,5.0,4.0,2.0,1.0,2.0,4.0,5.0,4.0,2.0,1.0,1.0,3.0,5.0,5.0,3.0,1.0,1.0,3.0,5.0,5.0,4.0,2.0,1.0],"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62]}}'
```

Resultado observado: **200** · «Coeficientes» y 1 sección más · 3 filas · 1 gráfico

### Control de calidad

#### `Carta X̄-R` — `chart-xbar-r`

Cartas de medias y rangos para subgrupos de tamaño constante.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Mediciones (por subgrupos consecutivos) |
| `size` | integer · por defecto `5` | sí | Tamaño del subgrupo |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-xbar-r/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"size":5}}'
```

Resultado observado: **200** · «Límites X̄» y 2 secciones más · 1 fila · 2 gráficos

#### `Carta I-MR` — `chart-imr`

Carta de individuos y rango móvil.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Observaciones |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-imr/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]}}'
```

Resultado observado: **200** · «Carta I» y 1 sección más · 1 fila · 2 gráficos

#### `Carta P` — `chart-p`

Proporción de defectuosos con subgrupos de distinto tamaño.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `defective` | numérico | sí | Defectuosos |
| `sample` | numérico | sí | Tamaño de muestra |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-p/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"defective":[3,4,2,5,3,4,1,3,4,2],"sample":[50,50,50,50,50,50,50,50,50,50]}}'
```

Resultado observado: **200** · «Límites» y 1 sección más · 1 fila · 1 gráfico

#### `Carta C` — `chart-c`

Número de defectos por unidad constante.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `defects` | numérico | sí | Defectos |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-c/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"defects":[5,7,3,8,6,4,7,5,6,9]}}'
```

Resultado observado: **200** · «Límites» · 1 fila · 1 gráfico

#### `Carta U` — `chart-u`

Defectos por unidad cuando el tamaño de la unidad varía.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `defects` | numérico | sí | Defectos |
| `units` | numérico | sí | Unidades inspeccionadas |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-u/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"defects":[5,7,3,8,6,4,7,5,6,9],"units":[1,1,1,1,1,1,1,1,1,1]}}'
```

Resultado observado: **200** · «Límites» · 1 fila · 1 gráfico

#### `Carta EWMA` — `chart-ewma`

Media móvil exponencialmente ponderada.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Observaciones |
| `lambda` | number · por defecto `0.2` | sí | Peso λ |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-ewma/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"lambda":0.2}}'
```

Resultado observado: **200** · «Límites» · 1 fila · 1 gráfico

#### `Carta CUSUM` — `chart-cusum`

Sumas acumuladas tabulares (k = 0,5).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Observaciones |
| `k` | number · por defecto `0.5` | sí | Valor de referencia k |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/chart-cusum/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"k":0.5}}'
```

Resultado observado: **200** · «Resumen» · 1 fila · 1 gráfico

### Capacidad

#### `Análisis de capacidad` — `capability`

Cp, Cpk, Pp, Ppk y PPM esperados.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Mediciones |
| `lsl` | number | no | Límite inferior (vacío = sin límite) |
| `usl` | number | no | Límite superior (vacío = sin límite) |
| `method` | select (normal|nonnormal) · por defecto `normal` | sí | Método |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/capability/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"lsl":6.0,"usl":14.0,"method":"normal"}}'
```

Resultado observado: **200** · «Índices» y 2 secciones más · 1 fila · 1 gráfico

### Sistemas de medida

#### `Gage R&R (cruzado)` — `gage-rr`

Estudio de repetibilidad y reproducibilidad por ANOVA.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `value` | numérico | sí | Medición |
| `part` | texto | sí | Pieza |
| `operator` | texto | sí | Operador |
| `trial` | number | no | Columna de repetición (opcional) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/gage-rr/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"value":[10.6,10.85,10.85,11.0,11.25,11.25,11.2,11.2,11.45,11.6,11.6,11.85,11.8,11.8,11.8,12.2,12.2,12.2]},"text":{"part":["P1","P1","P1","P1","P1","P1","P2","P2","P2","P2","P2","P2","P3","P3","P3","P3","P3","P3"],"operator":["Ana","Ana","Ana","Luis","Luis","Luis","Ana","Ana","Ana","Luis","Luis","Luis","Ana","Ana","Ana","Luis","Luis","Luis"]}}'
```

Resultado observado: **400** · error INVALID_INPUT · 0 filas · 0 gráficos

> **Nota:** El motor original devuelve este mismo error: la definición del análisis pasa la repetición como índice de fila, así que el diseño nunca queda balanceado (exigiría p·o·filas mediciones). Es un defecto heredado y verificado por paridad; el cálculo de Gage R&R existe en el dominio, pero este análisis del catálogo no es usable con datos reales.

### DOE

#### `Analizar diseño factorial (efectos)` — `doe-analyze`

Ajusta un modelo con factores codificados (−1/+1) y sus interacciones de 2 vías.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `y` | numérico | sí | Respuesta |
| `x` | numérico (una o varias columnas) | sí | Factores codificados |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/doe-analyze/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"y":[7.8,8.4,8.6,9.2,10.0,10.6,12.4,13.0,7.8,8.4,8.6,9.2,10.0,10.6,12.4,13.0],"A":[-1,-1,-1,-1,1,1,1,1,-1,-1,-1,-1,1,1,1,1],"B":[-1,-1,1,1,-1,-1,1,1,-1,-1,1,1,-1,-1,1,1],"C":[-1,1,-1,1,-1,1,-1,1,-1,1,-1,1,-1,1,-1,1]}}'
```

Resultado observado: **200** · «Efectos estimados» y 1 sección más · 7 filas · 1 gráfico

### Series temporales

#### `Función de autocorrelación (ACF)` — `timeseries-acf`

Autocorrelaciones y banda de confianza.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `lags` | integer · por defecto `20` | sí | Número de rezagos |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-acf/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"lags":20}}'
```

Resultado observado: **200** · «ACF» · 21 filas · 1 gráfico

#### `Autocorrelación parcial (PACF)` — `timeseries-pacf`

PACF por Durbin-Levinson.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `lags` | integer · por defecto `20` | sí | Número de rezagos |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-pacf/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"lags":20}}'
```

Resultado observado: **200** · «PACF» · 21 filas · 1 gráfico

#### `Media móvil` — `timeseries-moving-average`

Suavizado por media móvil centrada.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `window` | integer · por defecto `3` | sí | Ventana |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-moving-average/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"window":3}}'
```

Resultado observado: **200** · «Ajuste» · 30 filas · 1 gráfico

#### `Suavizado exponencial simple` — `timeseries-ses`

Suavizado exponencial simple con pronóstico a un paso.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `alpha` | number · por defecto `0.2` | sí | α |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-ses/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"alpha":0.2}}'
```

Resultado observado: **200** · «Resumen» · 1 fila · 1 gráfico

#### `Suavizado exponencial doble` — `timeseries-des`

Suavizado de Holt (nivel y tendencia).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `alpha` | number · por defecto `0.2` | sí | α |
| `beta` | number · por defecto `0.1` | sí | β |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-des/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"alpha":0.2,"beta":0.1}}'
```

Resultado observado: **200** · «Pronóstico» · 1 fila · 1 gráfico

#### `Suavizado de Winters` — `timeseries-winters`

Suavizado estacional (nivel, tendencia y estacionalidad).

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `period` | integer · por defecto `12` | sí | Periodo estacional |
| `alpha` | number · por defecto `0.2` | sí | α |
| `beta` | number · por defecto `0.1` | sí | β |
| `gamma` | number · por defecto `0.1` | sí | γ |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-winters/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"period":12,"alpha":0.2,"beta":0.1,"gamma":0.1}}'
```

Resultado observado: **200** · «Pronóstico» · 1 fila · 1 gráfico

#### `Descomposición estacional` — `timeseries-decompose`

Tendencia por media móvil centrada y patrón estacional.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `period` | integer · por defecto `12` | sí | Periodo |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-decompose/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"period":12}}'
```

Resultado observado: **200** · «Patrón estacional» · 12 filas · 1 gráfico

#### `Modelo AR y pronóstico` — `timeseries-ar`

Ajuste AR(p) por Yule-Walker con selección AIC y Ljung-Box.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Serie |
| `order` | integer · por defecto `3` | sí | Orden máximo |
| `horizon` | integer · por defecto `5` | sí | Horizonte de pronóstico |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/timeseries-ar/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]},"params":{"order":3,"horizon":5}}'
```

Resultado observado: **200** · «Modelo» y 3 secciones más · 1 fila · 1 gráfico

### Fiabilidad

#### `Ajuste de distribución Weibull` — `reliability-weibull`

Estimación por máxima verosimilitud y gráfico de probabilidad.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico | sí | Tiempos de falla |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/reliability-weibull/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]}}'
```

Resultado observado: **200** · «Parámetros (MLE)» y 1 sección más · 1 fila · 1 gráfico

#### `Kaplan-Meier` — `reliability-kaplan-meier`

Curva de supervivencia con censura.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `time` | numérico | sí | Tiempo |
| `event` | number | no | Evento observado (columna 0/1 opcional) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/reliability-kaplan-meier/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"time":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95]}}'
```

Resultado observado: **200** · «Supervivencia» y 1 sección más · 31 filas · 1 gráfico

### Multivariante

#### `Análisis de componentes principales` — `multivariate-pca`

PCA sobre la matriz de correlaciones con cargas y puntuaciones.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/multivariate-pca/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]}}'
```

Resultado observado: **200** · «Varianza explicada» y 1 sección más · 4 filas · 1 gráfico

#### `Análisis factorial` — `multivariate-factor`

Extracción por componentes principales con rotación varimax.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |
| `factors` | integer · por defecto `2` | sí | Número de factores |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/multivariate-factor/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]},"params":{"factors":2}}'
```

Resultado observado: **200** · «Cargas rotadas (varimax)» y 1 sección más · 4 filas · 1 gráfico

#### `Análisis de conglomerados K-means` — `multivariate-kmeans`

Agrupamiento k-means++ sobre variables estandarizadas.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |
| `k` | integer · por defecto `3` | sí | Número de grupos |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/multivariate-kmeans/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x1":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x2":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62],"x3":[7.837,7.864,7.556,7.197,7.116,7.388,7.763,7.896,7.665,7.282,7.1,7.285,7.668,7.896,7.76,7.385,7.115,7.2,7.56,7.865,7.835,7.496,7.162,7.138,7.447,7.805,7.883,7.608,7.235,7.105]},"params":{"k":3}}'
```

Resultado observado: **200** · «Resumen» y 1 sección más · 1 fila · 1 gráfico

#### `Análisis discriminante (2 grupos)` — `multivariate-lda`

Función discriminante lineal y exactitud de clasificación.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `x` | numérico (una o varias columnas) | sí | Variables |
| `group` | texto | sí | Grupo (2 niveles) |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/multivariate-lda/run" -H "Content-Type: application/json" --data-raw '{"numeric":{"x1":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2,9.9,10.5,9.4,11.0,10.1,9.7,10.8,9.5,10.3,10.6,9.2,11.1,10.05,9.85,10.35,9.75,10.55,9.65,10.45,9.95],"x2":[21.3,20.1,22.4,19.8,21.9,20.6,22.0,20.9,21.1,21.6,20.4,22.2,20.8,21.4,21.0,20.2,22.1,21.8,20.5,21.5,20.7,22.3,21.2,20.3,21.7,22.5,20.0,21.25,20.75,21.05],"x3":[5.2,4.8,5.6,5.0,4.6,5.4,4.9,5.3,5.1,4.7,5.5,5.05,4.95,5.35,4.85,5.15,4.75,5.45,5.25,4.65,5.55,5.02,4.92,5.32,4.82,5.12,4.72,5.42,5.22,4.62]},"text":{"group":["B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A","B","A"]}}'
```

Resultado observado: **200** · «Coeficientes» y 1 sección más · 3 filas · 1 gráfico

### Potencia y tamaño de muestra

#### `Potencia y tamaño de muestra (t de 1 muestra)` — `power-t1`

Potencia para un efecto y n dados, y n necesario para una potencia objetivo.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `effect` | number · por defecto `0.5` | sí | Tamaño del efecto (d) |
| `n` | integer · por defecto `20` | sí | Tamaño de muestra |
| `power` | number · por defecto `0.8` | sí | Potencia objetivo |
| `alpha` | number · por defecto `0.05` | sí | α |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/power-t1/run" -H "Content-Type: application/json" --data-raw '{"params":{"effect":0.5,"n":20,"power":0.8,"alpha":0.05}}'
```

Resultado observado: **200** · «Potencia» y 1 sección más · 1 fila · 1 gráfico

#### `Potencia y tamaño de muestra (proporción)` — `power-prop1`

Potencia para una prueba de una proporción.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `p0` | number · por defecto `0.5` | sí | Proporción nula |
| `p1` | number · por defecto `0.6` | sí | Proporción alternativa |
| `n` | integer · por defecto `100` | sí | Tamaño de muestra |
| `power` | number · por defecto `0.8` | sí | Potencia objetivo |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/power-prop1/run" -H "Content-Type: application/json" --data-raw '{"params":{"p0":0.5,"p1":0.6,"n":100,"power":0.8}}'
```

Resultado observado: **200** · «Potencia» y 1 sección más · 1 fila · 1 gráfico

#### `Potencia y tamaño de muestra (ANOVA de 1 factor)` — `power-anova1`

Aproximación para k grupos con efecto f de Cohen.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `effect` | number · por defecto `0.25` | sí | Tamaño del efecto (f) |
| `groups` | integer · por defecto `3` | sí | Número de grupos |
| `n` | integer · por defecto `20` | sí | n por grupo |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/power-anova1/run" -H "Content-Type: application/json" --data-raw '{"params":{"effect":0.25,"groups":3,"n":20}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

#### `Tamaño de muestra (t de 2 muestras)` — `power-t2`

n por grupo para detectar un efecto con potencia objetivo.

| Entrada | Tipo | Obligatoria | Etiqueta |
| --- | --- | --- | --- |
| `effect` | number · por defecto `0.5` | sí | Tamaño del efecto (d) |
| `power` | number · por defecto `0.8` | sí | Potencia objetivo |

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/analyses/power-t2/run" -H "Content-Type: application/json" --data-raw '{"params":{"effect":0.5,"power":0.8}}'
```

Resultado observado: **200** · «Resultado» · 1 fila · 1 gráfico

## 7. Distribuciones

`GET /api/v1/distributions` devuelve las 21 distribuciones con su tipo y parámetros.

| `id` | Nombre | Tipo | Parámetros (clave: etiqueta = por defecto) |
| --- | --- | --- | --- |
| `normal` | Normal | continuous | `mu`: Media (μ) = 0 · `sigma`: Desviación estándar (σ) = 1 |
| `t` | t de Student | continuous | `df`: Grados de libertad = 10 |
| `chi2` | Chi-cuadrada | continuous | `df`: Grados de libertad = 5 |
| `f` | F de Fisher | continuous | `df1`: Grados de libertad numerador = 5 · `df2`: Grados de libertad denominador = 10 |
| `gamma` | Gamma | continuous | `shape`: Forma = 2 · `scale`: Escala = 1 |
| `beta` | Beta | continuous | `a`: Alfa (α) = 2 · `b`: Beta (β) = 2 |
| `exponential` | Exponencial | continuous | `rate`: Tasa (λ) = 1 |
| `weibull` | Weibull | continuous | `shape`: Forma = 1.5 · `scale`: Escala = 1 |
| `lognormal` | Log-normal | continuous | `meanLog`: Media de log (μ) = 0 · `sdLog`: Desviación de log (σ) = 1 |
| `uniform` | Uniforme continua | continuous | `a`: Mínimo = 0 · `b`: Máximo = 1 |
| `logistic` | Logística | continuous | `mu`: Ubicación = 0 · `s`: Escala = 1 |
| `cauchy` | Cauchy | continuous | `mu`: Ubicación = 0 · `s`: Escala = 1 |
| `laplace` | Laplace | continuous | `mu`: Ubicación = 0 · `b`: Escala = 1 |
| `triangular` | Triangular | continuous | `a`: Mínimo = 0 · `c`: Moda = 0.5 · `b`: Máximo = 1 |
| `smallestExtreme` | Valor extremo menor (Gumbel) | continuous | `mu`: Ubicación = 0 · `s`: Escala = 1 |
| `largestExtreme` | Valor extremo mayor (Gumbel) | continuous | `mu`: Ubicación = 0 · `s`: Escala = 1 |
| `foldedNormal` | Normal plegada | continuous | `mu`: Media (μ) = 0 · `sigma`: Desviación (σ) = 1 |
| `binomial` | Binomial | discrete | `n`: Número de ensayos = 10 · `p`: Probabilidad de éxito = 0.5 |
| `poisson` | Poisson | discrete | `lambda`: Media (λ) = 3 |
| `hypergeometric` | Hipergeométrica | discrete | `population`: Tamaño de población (N) = 50 · `successes`: Éxitos en la población (K) = 10 · `sample`: Tamaño de muestra (n) = 5 |
| `negativeBinomial` | Binomial negativa | discrete | `r`: Número de éxitos (r) = 5 · `p`: Probabilidad de éxito = 0.5 |

### Evaluar (`POST /api/v1/distributions/{id}/evaluate`)

Calcula pdf/pmf, cdf e inversa para las listas de `x` y `probabilities` enviadas, más los momentos
teóricos y el soporte. Los parámetros omitidos toman su valor por defecto.

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/distributions/normal/evaluate" `
  -H "Content-Type: application/json" `
  --data-raw '{"params":{"mu":0,"sigma":1},"x":[-1,0,1],"probabilities":[0.5,0.975]}'
```

```jsonc
{
  "pdf": [0.24197072451914337, 0.3989422804014327, 0.24197072451914337],
  "cdf": [0.15865525393145707, 0.5, 0.8413447460685429],
  "invCdf": [0, 1.959963984540054],
  "mean": 0,
  "sd": 1,
  "support": { "lower": "-Infinity", "upper": "Infinity" }
}
```

### Muestrear (`POST /api/v1/distributions/{id}/sample`)

```powershell
curl.exe -X POST "http://localhost:5080/api/v1/distributions/gamma/sample" `
  -H "Content-Type: application/json" `
  --data-raw '{"params":{"shape":2,"scale":1},"count":5,"seed":20261023}'
```

La semilla por defecto es `20261023`; con la misma semilla la serie es idéntica.

### Una petición verificada por distribución

| `id` | Cuerpo de `evaluate` | pdf(0) observado | cdf(0) observado |
| --- | --- | --- | --- |
| `normal` | `{"params":{"mu":0,"sigma":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.3989422804014327 | 0.5 |
| `t` | `{"params":{"df":10},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.38910838396603187 | 0.5 |
| `chi2` | `{"params":{"df":5},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `f` | `{"params":{"df1":5,"df2":10},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `gamma` | `{"params":{"shape":2,"scale":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `beta` | `{"params":{"a":2,"b":2},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `exponential` | `{"params":{"rate":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 1 | 0 |
| `weibull` | `{"params":{"shape":1.5,"scale":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `lognormal` | `{"params":{"meanLog":0,"sdLog":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `uniform` | `{"params":{"a":0,"b":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 1 | 0 |
| `logistic` | `{"params":{"mu":0,"s":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.25 | 0.5 |
| `cauchy` | `{"params":{"mu":0,"s":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.3183098861837907 | 0.5 |
| `laplace` | `{"params":{"mu":0,"b":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.5 | 0.5 |
| `triangular` | `{"params":{"a":0,"c":0.5,"b":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0 | 0 |
| `smallestExtreme` | `{"params":{"mu":0,"s":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.36787944117144233 | 0.6321205588285577 |
| `largestExtreme` | `{"params":{"mu":0,"s":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.36787944117144233 | 0.36787944117144233 |
| `foldedNormal` | `{"params":{"mu":0,"sigma":1},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.7978845608028654 | 0 |
| `binomial` | `{"params":{"n":10,"p":0.5},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.0009765625 | 0.0009765624999999985 |
| `poisson` | `{"params":{"lambda":3},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.049787068367863986 | 0.049787068367863986 |
| `hypergeometric` | `{"params":{"population":50,"successes":10,"sample":5},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.31056278200456683 | 0.31056278200456683 |
| `negativeBinomial` | `{"params":{"r":5,"p":0.5},"x":[0,1],"probabilities":[0.5,0.975]}` | 0.03125000000000003 | 0.03125000000000003 |

## 8. Errores

| `code` | HTTP | Significado |
| --- | --- | --- |
| `INVALID_INPUT` | 400 | La entrada no es válida para el análisis |
| `INVALID_PARAMETER` | 400 | Parámetro fuera de rango o ausente |
| `TYPE_MISMATCH` | 400 | El tipo del dato no corresponde |
| `INSUFFICIENT_DATA` | 422 | No hay observaciones suficientes |
| `SINGULAR_MATRIX` | 422 | El sistema lineal no tiene solución |
| `NON_CONVERGENCE` | 422 | El ajuste iterativo no convergió |
| `NOT_SUPPORTED` | 422 | Combinación no soportada por el motor |
| `NOT_FOUND` | 404 | Análisis, distribución o ruta desconocidos |
| `API_PAYLOAD_TOO_LARGE` | 413 | Se superó un límite de carga |
| `API_RATE_LIMITED` | 429 | Límite de peticiones |
| `API_TIMEOUT` | 504 | Tiempo máximo de petición |
| `API_UNAUTHORIZED` | 401 | `X-Api-Key` ausente o incorrecta (si está configurada) |

```jsonc
{
  "type": "https://statisticengine.local/errors/insufficient_data",
  "title": "Se necesitan al menos 2 observaciones.",
  "status": 422,
  "code": "INSUFFICIENT_DATA",
  "traceId": "00-...-00",
  "analysisId": "one-sample-t"
}
```

La validación previa (`POST .../validate`) devuelve siempre `200` con el motivo:

```jsonc
{ "valid": false, "error": { "code": "INSUFFICIENT_DATA", "message": "Falta \"Muestra\"." } }
```

## 9. Límites y configuración

| Límite | Valor efectivo |
| --- | --- |
| `maxNumericValues` | `1000000` |
| `maxColumnLength` | `250000` |
| `maxTextCharacters` | `2000000` |
| `maxParameters` | `50` |
| `maxDistributionPoints` | `10000` |
| `maxSampleCount` | `100000` |
| `maxRequestBodyBytes` | `33554432` |
| `rateLimitEnabled` | `False` |
| `rateLimitPermitLimit` | `60` |
| `rateLimitWindowSeconds` | `60` |

Tiempo máximo por petición: **30 s**. Versión del motor: **1.0.0** · baseline de paridad: `tinytab@0.1.0/src/domain`.

Todo se configura por entorno (`StatisticEngine__Limits__MaxNumericValues`, …); ver
`README.md` §5.

## 10. Determinismo y paridad

- Los análisis son funciones puras de la entrada: mismas columnas, mismos parámetros, mismo
  resultado.
- El RNG (`distributions/{id}/sample`) es `xoshiro128**`, idéntico bit a bit al del motor
  TypeScript; la semilla viaja en el cuerpo.
- Las respuestas coinciden con el motor TypeScript de TinyTab: 29 pruebas de dominio comparan
  secciones, gráficos, errores y texto de sesión; ver `PARITY-NOTES.md`.

## 11. Cliente HTTP de Visual Studio / Rider

En `examples/` hay dos colecciones listas para ejecutar:

- `statisticengine.http`: REST Client de Visual Studio 2022/2026, JetBrains o VS Code.
- `StatisticEngine.postman_collection.json`: colección Postman v2.1 con todos los endpoints,
  los 60 análisis y las 21 distribuciones (evaluate y sample), con tests automáticos.
