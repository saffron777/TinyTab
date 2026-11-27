# StatisticEngine

Microservicio HTTP en **C# / ASP.NET Core 10** que expone el **motor estadístico de TinyTab**
como API REST versionada: 21 distribuciones, los **60 análisis baseline** con paridad numérica
verificada contra el motor TypeScript y una ampliación creciente de análisis y operaciones de
datos portados desde **Sigma Algebra** (origen `sigma`, ver [EXTENSIONS-NOTES.md](EXTENSIONS-NOTES.md)).

- **Autónomo y sin estado:** no usa Node, base de datos ni estado en memoria entre peticiones.
- **Puerto nativo:** reimplementación 1:1 en C#; no invoca el motor TypeScript ni Python en ejecución.
- **Paridad verificada:** el baseline se compara con el motor TypeScript; las extensiones, con
  vectores generados desde Sigma (ver `tools/sigma-golden`).
- **Licencia:** GPL-3.0-or-later (heredada de TinyTab).

## 1. Estructura

```
services/StatisticEngine/
  src/StatisticEngine.Domain/        motor puro: sin HTTP, sin I/O
    Numerics/ Random/ Distributions/ Descriptive/ Tables/ Inference/
    Models/ Quality/ Msa/ Doe/ TimeSeries/ Reliability/ Power/ Multivariate/
    Charts/ Analysis/ Data/ Statistics/
    Analysis/Analyses/Extensions/    análisis portados desde Sigma
  src/StatisticEngine.Application/   casos de uso (ejecutar, validar, catálogo, distribuciones, datos)
  src/StatisticEngine.Api/           host HTTP: endpoints, contratos, límites, middleware
  tests/StatisticEngine.Domain.Tests/  paridad baseline y extensiones (Sigma) + unitarias
  tests/StatisticEngine.Api.Tests/     pruebas de contrato HTTP
  tools/golden/                      generador de vectores dorados (ejecuta el motor TS)
  tools/sigma-golden/                generador de vectores desde Sigma (sólo desarrollo)
```

Regla de dependencia: `Api → Application → Domain`. `Domain` no conoce ASP.NET, ni Kestrel, ni I/O.

## 2. Arranque rápido

Requisitos: .NET SDK 10.0.402 (fijado por `global.json`).

```powershell
cd services/StatisticEngine
dotnet run --project src/StatisticEngine.Api      # perfil "StatisticEngine.Api": https://localhost:54236
dotnet run --project src/StatisticEngine.Api --launch-profile "StatisticEngine.Api (HTTP)"   # http://localhost:5080
dotnet test                                       # suite de dominio + API (62 pruebas en la fase 1)
```

La URL no se fija en `appsettings.json` a propósito: la controlan el perfil de lanzamiento
(Visual Studio o `dotnet run`), `--urls` o `ASPNETCORE_URLS`, para que ningún valor de
configuración pise al perfil del IDE.

Contenedor:

```powershell
docker compose up --build        # http://127.0.0.1:5080
```

Probes y documentación:

| Recurso | URL |
| --- | --- |
| Página de inicio con enlaces | `GET /` |
| Documentación interactiva (Scalar) | `GET /scalar/v1` |
| Contrato OpenAPI | `GET /openapi/v1.json` |
| Inventario de rutas publicadas | `GET /api/v1/endpoints` |
| Liveness | `GET /health/live` |
| Readiness (autotest numérico) | `GET /health/ready` |

- **[API.md](API.md)**: guía completa con ejemplos ejecutables de los endpoints, los análisis
  publicados y las 21 distribuciones (generada desde el propio servicio).
- **[examples/StatisticEngine.postman_collection.json](examples/StatisticEngine.postman_collection.json)**:
  colección **Postman v2.1** con peticiones (descubrimiento, catálogo, los análisis por
  categoría, las 21 distribuciones × `evaluate`/`sample`, errores y un flujo encadenado) y tests
  automáticos en cada una. Importar con *File → Import* y ajustar la variable `baseUrl`.
- **[examples/statisticengine.http](examples/statisticengine.http)**: colección lista para el
  REST Client de Visual Studio / JetBrains HTTP Client / VS Code.
- **[deploy/README.md](deploy/README.md)**: despliegue detrás de **Apache** como proxy inverso,
  servicio de Windows (NSSM), unidad de systemd y contenedores Apache + servicio.

## 3. API v1

Base: `/api/v1`. Todas las respuestas son JSON camelCase; los errores son
`application/problem+json` con extensión `code` (y `traceId`, `analysisId`).

### Catálogo

```powershell
curl http://127.0.0.1:5080/api/v1/analyses
curl "http://127.0.0.1:5080/api/v1/analyses?category=ANOVA"
curl http://127.0.0.1:5080/api/v1/analyses/describe
```

### Ejecutar un análisis

```powershell
curl -X POST http://127.0.0.1:5080/api/v1/analyses/describe/run `
  -H "Content-Type: application/json" `
  -d '{"numeric":{"x":[1,2,3,4,5]},"params":{"confidence":0.95}}'
```

```jsonc
{
  "id": "describe",
  "title": "Estadísticas descriptivas",
  "subtitle": "Intervalo de confianza del 95.0 %",
  "sections": [
    { "kind": "table", "title": "Resumen", "headers": ["Variable", "N", "Media", "..."],
      "rows": [["x", "5", "3", "0.7071", "1.5811", "1", "2", "3", "4", "5", "2", "1.0368 – 4.9632"]] }
  ],
  "charts": []
}
```

- `?format=text` devuelve `text/plain` con el mismo texto que la ventana de sesión de TinyTab.
- `POST /api/v1/analyses/{id}/validate` devuelve `200 { valid, error? }` sin ejecutar.
- Los `charts` usan las 11 variantes del motor (`histogram`, `control`, `pca`, …).
- Los valores no finitos se serializan como `"NaN"`, `"Infinity"` y `"-Infinity"`.

### Distribuciones

```powershell
curl http://127.0.0.1:5080/api/v1/distributions
curl -X POST http://127.0.0.1:5080/api/v1/distributions/normal/evaluate `
  -H "Content-Type: application/json" `
  -d '{"params":{"mu":0,"sigma":1},"x":[-1,0,1],"probabilities":[0.975]}'
curl -X POST http://127.0.0.1:5080/api/v1/distributions/normal/sample `
  -H "Content-Type: application/json" -d '{"count":5,"seed":20261023}'
```

El muestreo es **determinista** para la misma semilla (xoshiro128** idéntico al motor).

### Operaciones de datos

`GET /api/v1/data` publica las operaciones sobre frames tipados. El cuerpo usa `columns`
(`{name, kind, values}` con `kind` = `numeric`, `text` o `datetime`), `datasets` auxiliares y
`params`; la respuesta incluye las secciones del informe y los `datasets` resultantes:

```powershell
curl -X POST http://127.0.0.1:5080/api/v1/data/data-standardize/run `
  -H "Content-Type: application/json" `
  -d '{"columns":[{"name":"x1","kind":"numeric","values":[1,2,3,null,5]}],
       "roles":{"cols":["x1"]},"params":{"method":"zscore"}}'
```

`POST /api/v1/data/{id}/validate` valida sin ejecutar. Los análisis extendidos aceptan además
`columns`, `roles` (rol → columna(s)) y `datetime`, y publican `sections` con los tipos
`heading`, `tiles` y `alert`, más `datasets` y `keyResults`.

### Operación

`GET /api/v1/meta` devuelve versión, baseline de paridad y límites efectivos.
`GET /api/v1/endpoints` enumera las rutas realmente publicadas (se leen del enrutador en caliente,
no de una lista fija). La raíz `/` sirve una página HTML con enlaces a todo lo anterior.
Cabecera `X-StatisticEngine-Version` en todas las respuestas.

## 4. Errores

| `code` | HTTP | Cuándo |
| --- | --- | --- |
| `INVALID_INPUT`, `INVALID_PARAMETER`, `TYPE_MISMATCH` | 400 | Entrada inválida |
| `INSUFFICIENT_DATA`, `SINGULAR_MATRIX`, `NON_CONVERGENCE`, `NOT_SUPPORTED` | 422 | El motor no puede calcular |
| `NOT_FOUND` | 404 | Análisis, distribución o ruta desconocidos |
| `API_PAYLOAD_TOO_LARGE` | 413 | Columnas, valores o parámetros por encima del límite |
| `API_RATE_LIMITED` | 429 | Límite de peticiones |
| `API_TIMEOUT` | 504 | Tiempo máximo de petición |
| `API_UNAUTHORIZED` | 401 | `X-Api-Key` ausente o incorrecta (si está configurada) |
| `UNKNOWN`, `HTTP_<status>` | 500 / otros | Fallo interno o estado sin mapear |

## 5. Configuración

Sección `StatisticEngine` de `appsettings.json` o variables de entorno
(`StatisticEngine__Limits__MaxNumericValues`, …):

| Clave | Por defecto | Descripción |
| --- | --- | --- |
| `Limits:MaxNumericValues` | 1 000 000 | Valores numéricos totales por petición |
| `Limits:MaxColumnLength` | 250 000 | Valores por columna |
| `Limits:MaxTextCharacters` | 2 000 000 | Caracteres de columnas de texto |
| `Limits:MaxParameters` | 50 | Parámetros por petición |
| `Limits:MaxDistributionPoints` | 10 000 | Puntos en `evaluate` |
| `Limits:MaxSampleCount` | 100 000 | Muestras por petición |
| `Limits:MaxDataRows` | 250 000 | Filas de un frame tipado |
| `Limits:MaxDatasetValues` | 4 000 000 | Valores totales de un frame o dataset |
| `Limits:MaxTableRows` | 20 000 | Filas máximas de una tabla de salida |
| `Limits:MaxChartPoints` | 10 000 | Puntos máximos de una serie de gráfico |
| `Limits:MaxExpressionLength` | 1 000 | Longitud máxima de una expresión |
| `Limits:HeavyRequestTimeoutSeconds` | 300 | Tiempo máximo de los análisis marcados como pesados |
| `Limits:MaxRequestBodyBytes` | 33 554 432 | Cuerpo máximo (32 MiB, coherente con 1 M de valores) |
| `RateLimit:Enabled` / `PermitLimit` / `WindowSeconds` | `true` / 60 / 60 | Ventana fija por IP |
| `Auth:ApiKey` | vacío | Si se define, exige `X-Api-Key` (probes exentos) |
| `Cors:AllowedOrigins` | vacío | Lista de orígenes; vacío deshabilita CORS |
| `Proxy:Enabled` | `false` | Confía en `X-Forwarded-For/Proto` (solo detrás de proxy propio) |
| `ApiExplorer:Enabled` | `true` | Publica OpenAPI (`/openapi/v1.json`) y Scalar (`/scalar/v1`) |
| `RequestTimeoutSeconds` | 30 | Responde 504 al superarlo |
| *(sin `Urls` en appsettings)* | — | La URL la fija el perfil de lanzamiento, `--urls` o `ASPNETCORE_URLS` (en contenedor: `http://+:8080`) |

## 6. Paridad con el motor TypeScript y con Sigma

Los vectores dorados del **baseline** (`tests/StatisticEngine.Domain.Tests/Parity/vectors/*.json`)
se generan ejecutando el motor TypeScript original:

```powershell
npx vitest run --config services/StatisticEngine/tools/golden/vitest.config.ts
```

Los vectores de las **extensiones Sigma** (`tests/StatisticEngine.Domain.Tests/Extensions/vectors/*.json`)
se generan con `tools/sigma-golden/generate.py` y el venv de Sigma; incluyen los ejemplos declarados
de cada análisis, los casos de error de validación y los metadatos. Detalles, tolerancias y
desviaciones: [EXTENSIONS-NOTES.md](EXTENSIONS-NOTES.md).

Las pruebas comparan: textos exactos (tablas y formato de sesión), claves exactas, errores
exactos, RNG bit a bit y dobles con tolerancia `1e-12` (`1e-9` en ajustes iterativos). Detalles,
defectos reproducidos y desviaciones documentadas: [PARITY-NOTES.md](PARITY-NOTES.md).

## 7. Regenerar la documentación del API

`API.md` y `examples/statisticengine.http` se generan consultando el servicio en ejecución, de
modo que las columnas, parámetros, estados y respuestas coinciden siempre con la implementación:

```powershell
# terminal 1
dotnet run --project src/StatisticEngine.Api --launch-profile "StatisticEngine.Api (HTTP)"

# terminal 2
pwsh ./tools/docs/generate-api-docs.ps1 -BaseUrl http://localhost:5080
```

El script ejecuta una petición real por cada análisis publicado y por cada distribución, y deja
constancia de los que devuelven error. La regeneración de la colección Postman con las
operaciones de datos se hará al cerrar la fase 5 (ver `EXTENSIONS-NOTES.md`).

## 8. Despliegue detrás de Apache

Apache (`httpd`) no ejecuta ASP.NET Core; se usa como **proxy inverso** y el servicio escucha solo
en loopback. Guía completa, vhosts y scripts en **[deploy/README.md](deploy/README.md)**:

```powershell
# 1) publicar e instalar como servicio de Windows (requiere NSSM y consola de administrador)
.\deploy\windows\install-service.ps1 -Action install -ApiKey "una-clave-larga"

# 2) Apache: copiar deploy\apache\statisticengine.conf a conf\extra y añadir el Include
C:\Apache24\bin\httpd.exe -t
Restart-Service Apache2.4
```

Lo que no debe faltar:

| Ajuste | Valor | Motivo |
| --- | --- | --- |
| `StatisticEngine__Proxy__Enabled` | `true` | Sin esto el rate limit ve solo la IP de Apache y se ignoran `X-Forwarded-*` |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` | El servicio no se expone directamente |
| `ProxyTimeout` (Apache) | `330` | Los análisis usan el timeout `heavy` (300 s) |
| `LimitRequestBody` (Apache) | `33554432` | Coherente con `Limits:MaxRequestBodyBytes` (32 MiB) |
| Topología | **subdominio** | Scalar/OpenAPI usan rutas absolutas; en subruta expón solo `/api` y `/health` |

Servicio en Linux: [deploy/linux/statisticengine.service](deploy/linux/statisticengine.service).
Contenedores con Apache delante: [deploy/docker/docker-compose.apache.yml](deploy/docker/docker-compose.apache.yml).

## 9. Verificación

```powershell
cd services/StatisticEngine
dotnet build StatisticEngine.slnx -c Release -warnaserror
dotnet test StatisticEngine.slnx -c Release
```

En Windows, el sandbox de archivos de este entorno falla al aplicar ACL sobre `E:\TinyTab`;
los comandos anteriores deben ejecutarse con acceso completo (`danger-full-access`).

## 10. Alcance

Incluye el motor estadístico y su catálogo, incluidas las extensiones portadas desde Sigma y sus
operaciones de datos. **No** incluye hoja de trabajo, proyectos `.ttab`, importación/exportación
CSV, HTML de sesión ni el lenguaje de comandos: son capas de la aplicación de escritorio, no del
motor. Machine learning y ecuaciones estructurales de Sigma quedan fuera de esta ampliación
(ver `EXTENSIONS-NOTES.md`).
