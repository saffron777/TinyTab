# StatisticEngine

Microservicio HTTP en **C# / ASP.NET Core 10** que expone el **motor estadístico de TinyTab**
(21 distribuciones y 60 análisis) como API REST versionada, con paridad numérica verificada
contra el motor TypeScript original mediante **vectores dorados**.

- **Autónomo y sin estado:** no usa Node, base de datos ni estado en memoria entre peticiones.
- **Puerto nativo:** reimplementación 1:1 en C#; no invoca el motor TypeScript en ejecución.
- **Paridad verificada:** 29 pruebas de dominio comparan funciones, distribuciones y los 60
  análisis (secciones, gráficos, formato de texto y errores) contra el motor original.
- **Licencia:** GPL-3.0-or-later (heredada de TinyTab).

## 1. Estructura

```
services/StatisticEngine/
  src/StatisticEngine.Domain/        motor puro: sin HTTP, sin I/O
    Numerics/ Random/ Distributions/ Descriptive/ Tables/ Inference/
    Models/ Quality/ Msa/ Doe/ TimeSeries/ Reliability/ Power/ Multivariate/
    Charts/ Analysis/
  src/StatisticEngine.Application/   casos de uso (ejecutar, validar, catálogo, distribuciones)
  src/StatisticEngine.Api/           host HTTP: endpoints, contratos, límites, middleware
  tests/StatisticEngine.Domain.Tests/  29 pruebas de paridad y unitarias
  tests/StatisticEngine.Api.Tests/     19 pruebas de contrato HTTP
  tools/golden/                      generador de vectores dorados (ejecuta el motor TS)
```

Regla de dependencia: `Api → Application → Domain`. `Domain` no conoce ASP.NET, ni Kestrel, ni I/O.

## 2. Arranque rápido

Requisitos: .NET SDK 10.0.402 (fijado por `global.json`).

```powershell
cd services/StatisticEngine
dotnet run --project src/StatisticEngine.Api      # perfil "StatisticEngine.Api": https://localhost:54236
dotnet run --project src/StatisticEngine.Api --launch-profile "StatisticEngine.Api (HTTP)"   # http://localhost:5080
dotnet test                                       # 48 pruebas (29 dominio + 19 API)
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
| Liveness | `GET /health/live` |
| Readiness (autotest numérico) | `GET /health/ready` |
| OpenAPI | `GET /openapi/v1.json` |
| Scalar UI (solo Development) | `GET /scalar/v1` |

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

### Operación

`GET /api/v1/meta` devuelve versión, baseline de paridad y límites efectivos.
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
| `Limits:MaxRequestBodyBytes` | 33 554 432 | Cuerpo máximo (32 MiB, coherente con 1 M de valores) |
| `RateLimit:Enabled` / `PermitLimit` / `WindowSeconds` | `true` / 60 / 60 | Ventana fija por IP |
| `Auth:ApiKey` | vacío | Si se define, exige `X-Api-Key` (probes exentos) |
| `Cors:AllowedOrigins` | vacío | Lista de orígenes; vacío deshabilita CORS |
| `Proxy:Enabled` | `false` | Confía en `X-Forwarded-For/Proto` (solo detrás de proxy propio) |
| `RequestTimeoutSeconds` | 30 | Responde 504 al superarlo |
| *(sin `Urls` en appsettings)* | — | La URL la fija el perfil de lanzamiento, `--urls` o `ASPNETCORE_URLS` (en contenedor: `http://+:8080`) |

## 6. Paridad con el motor TypeScript

Los vectores dorados (`tests/StatisticEngine.Domain.Tests/Parity/vectors/*.json`) se generan
ejecutando el motor TypeScript original:

```powershell
npx vitest run --config services/StatisticEngine/tools/golden/vitest.config.ts
```

Las pruebas comparan: textos exactos (tablas y formato de sesión), claves exactas, errores
exactos, RNG bit a bit y dobles con tolerancia `1e-12` (`1e-9` en ajustes iterativos).
Detalles, defectos reproducidos y desviaciones documentadas: [PARITY-NOTES.md](PARITY-NOTES.md).

## 7. Verificación

```powershell
cd services/StatisticEngine
dotnet build StatisticEngine.slnx -c Release -warnaserror
dotnet test StatisticEngine.slnx -c Release
```

En Windows, el sandbox de archivos de este entorno falla al aplicar ACL sobre `E:\TinyTab`;
los comandos anteriores deben ejecutarse con acceso completo (`danger-full-access`).

## 8. Alcance

Incluye el motor estadístico y su catálogo. **No** incluye hoja de trabajo, proyectos `.ttab`,
importación/exportación CSV, HTML de sesión ni el lenguaje de comandos: son capas de la
aplicación de escritorio, no del motor.
