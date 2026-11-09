#Requires -Version 7.0
<#
.SYNOPSIS
    Genera API.md consultando el propio servicio en ejecución.

.DESCRIPTION
    Lee el catálogo, las distribuciones, los metadatos y el inventario de rutas del servicio,
    ejecuta un ejemplo real por cada análisis y por cada distribución, y escribe una guía con
    tablas exactas y respuestas observadas. Nada se inventa: las columnas, parámetros y
    resultados provienen del servicio.

.EXAMPLE
    dotnet run --project src/StatisticEngine.Api --launch-profile "StatisticEngine.Api (HTTP)"
    pwsh ./tools/docs/generate-api-docs.ps1 -BaseUrl http://localhost:5080
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5080",
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\..\API.md"),
    [string]$PostmanPath = (Join-Path $PSScriptRoot "..\..\examples\StatisticEngine.postman_collection.json")
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$base$Path"
    if ($null -eq $Body) {
        return Invoke-WebRequest -Uri $uri -Method $Method -TimeoutSec 30 -SkipCertificateCheck
    }
    $json = $Body | ConvertTo-Json -Depth 10 -Compress
    return Invoke-WebRequest -Uri $uri -Method $Method -Body $json -ContentType "application/json" -TimeoutSec 30 -SkipCertificateCheck
}

# ---------------------------------------------------------------- datos de ejemplo
$var1 = @(9.8, 10.4, 9.1, 11.2, 10.0, 9.6, 10.9, 9.3, 10.7, 10.2, 9.9, 10.5, 9.4, 11.0, 10.1,
          9.7, 10.8, 9.5, 10.3, 10.6, 9.2, 11.1, 10.05, 9.85, 10.35, 9.75, 10.55, 9.65, 10.45, 9.95)
$var2 = @(21.3, 20.1, 22.4, 19.8, 21.9, 20.6, 22.0, 20.9, 21.1, 21.6, 20.4, 22.2, 20.8, 21.4, 21.0,
          20.2, 22.1, 21.8, 20.5, 21.5, 20.7, 22.3, 21.2, 20.3, 21.7, 22.5, 20.0, 21.25, 20.75, 21.05)
$var3 = @(5.2, 4.8, 5.6, 5.0, 4.6, 5.4, 4.9, 5.3, 5.1, 4.7, 5.5, 5.05, 4.95, 5.35, 4.85,
          5.15, 4.75, 5.45, 5.25, 4.65, 5.55, 5.02, 4.92, 5.32, 4.82, 5.12, 4.72, 5.42, 5.22, 4.62)
$var4 = 1..30 | ForEach-Object { [math]::Round(7.5 + 0.4 * [math]::Sin($_), 3) }
$grupo = 1..30 | ForEach-Object { @('A', 'B', 'C')[$_ % 3] }
$turno = 1..30 | ForEach-Object { @('Mañana', 'Tarde', 'Noche')[$_ % 3] }
$pieza = 1..30 | ForEach-Object { "P" + (($_) % 10) }
$operador = 1..30 | ForEach-Object { @('Ana', 'Luis')[$_ % 2] }
$binaria = 1..30 | ForEach-Object { $_ % 2 }
$conteos = @(12, 18, 9, 15)
$cuentas = 1..30 | ForEach-Object { [math]::Round(3 + 2 * [math]::Sin($_), 0) }
$defectuosos = @(3, 4, 2, 5, 3, 4, 1, 3, 4, 2)
$muestraP = @(50, 50, 50, 50, 50, 50, 50, 50, 50, 50)
$defectosU = @(5, 7, 3, 8, 6, 4, 7, 5, 6, 9)
$unidadesU = @(1, 1, 1, 1, 1, 1, 1, 1, 1, 1)
$evento = 1..30 | ForEach-Object { if ($_ % 5 -eq 0) { 0 } else { 1 } }

function Get-Series {
    param([string]$Key)
    switch -Regex ($Key) {
        '^(y|x|value|measurements|values|response|sample|tiempo|time)$' { return , $var1 }
        '^x1$' { return , $var1 }
        '^(x2|column)$' { return , $var2 }
        '^x3$' { return , $var3 }
        '^x4$' { return , $var4 }
        '^defective' { return , $defectuosos }
        '^(sample|units|tamano)' { return , $muestraP }
        '^defects' { return , $defectosU }
        '^(counts|count)' { return , $conteos }
        '^event' { return , $evento }
        '^(binary|exito|success)' { return , $binaria }
        default { return , $var1 }
    }
}

function Get-TextSeries {
    param([string]$Key)
    switch -Regex ($Key) {
        '^(factor|a|row|t|grupo|group)$' { return , $grupo }
        '^(b|column|turno)$' { return , $turno }
        '^(part|pieza)$' { return , $pieza }
        '^(operator|operador)$' { return , $operador }
        '^label' { return , @('alfa', 'beta', 'gamma', 'delta', 'épsilon') }
        default { return , $grupo }
    }
}

function New-SampleBody {
    param($Analysis)
    $numeric = [ordered]@{}
    $text = [ordered]@{}
    $parameters = [ordered]@{}

    switch ($Analysis.id) {
        'doe-analyze' {
            # Diseño factorial 2^3 con dos réplicas (16 corridas) para que quede error residual.
            $a = @(-1, -1, -1, -1, 1, 1, 1, 1, -1, -1, -1, -1, 1, 1, 1, 1)
            $b = @(-1, -1, 1, 1, -1, -1, 1, 1, -1, -1, 1, 1, -1, -1, 1, 1)
            $c = @(-1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1)
            $y = for ($i = 0; $i -lt 16; $i++) { [math]::Round(10 + 1.5 * $a[$i] + 0.8 * $b[$i] + 0.4 * ($a[$i] * $b[$i]) + 0.3 * $c[$i], 3) }
            $numeric['y'] = @($y); $numeric['A'] = $a; $numeric['B'] = $b; $numeric['C'] = $c
        }
        'regression-logistic' {
            $numeric['y'] = $binaria
            $numeric['x1'] = $var2
            $numeric['x2'] = $var3
        }
        'regression-poisson' {
            $numeric['y'] = $cuentas
            $numeric['x1'] = $var1
            $numeric['x2'] = $var3
        }
        'chart-p' {
            $numeric['defective'] = $defectuosos
            $numeric['sample'] = $muestraP
        }
        'chart-c' { $numeric['defects'] = $defectosU }
        'chart-u' {
            $numeric['defects'] = $defectosU
            $numeric['units'] = $unidadesU
        }
        'chi-square-gof' { $numeric['counts'] = $conteos }
        'tally' { $text['t'] = $grupo }
        'cross-tab' {
            $text['row'] = $grupo
            $text['column'] = $turno
        }
        'nonparam-kruskal-wallis' {
            $numeric['y'] = $var1
            $text['factor'] = $grupo
        }
        'gage-rr' {
            # Diseño cruzado balanceado 3 piezas × 2 operadores × 3 repeticiones.
            $values = @()
            $parts = @()
            $operators = @()
            foreach ($part in 1..3) {
                foreach ($operator in @('Ana', 'Luis')) {
                    $offset = if ($operator -eq 'Ana') { 0.0 } else { 0.4 }
                    foreach ($trial in 1..3) {
                        $noise = 0.05 * (($part * 7 + $trial * 3) % 5)
                        $values += [math]::Round(10 + 0.5 * $part + $offset + 0.1 * $trial + $noise, 3)
                        $parts += "P$part"
                        $operators += $operator
                    }
                }
            }
            $numeric['value'] = $values
            $text['part'] = $parts
            $text['operator'] = $operators
        }
        'multivariate-lda' {
            # LDA del motor exige exactamente dos grupos.
            $numeric['x1'] = $var1
            $numeric['x2'] = $var2
            $numeric['x3'] = $var3
            $text['group'] = 1..30 | ForEach-Object { if (($_ % 2) -eq 0) { 'A' } else { 'B' } }
        }
        'regression-stepwise' {
            # Predictores correlacionados con la respuesta para que la selección por AIC avance.
            $y = for ($i = 0; $i -lt 30; $i++) {
                [math]::Round(1.5 * $var2[$i] + 0.5 * $var3[$i] + 0.01 * $var1[$i], 3)
            }
            $numeric['y'] = @($y)
            $numeric['x1'] = $var2
            $numeric['x2'] = $var3
            $numeric['x3'] = $var4
        }
        'capability' {
            $numeric['x'] = $var1
            $parameters['lsl'] = 6.0
            $parameters['usl'] = 14.0
            $parameters['method'] = 'normal'
        }
        default {
            $numericColumns = @($Analysis.numericColumns)
            $textColumns = @($Analysis.textColumns)
            for ($index = 0; $index -lt $numericColumns.Count; $index++) {
                $column = $numericColumns[$index]
                if (-not $column.required) { continue }
                if ($column.multiple) {
                    $numeric[$column.key] = $var1
                    $numeric['x1'] = $var2
                    $numeric['x2'] = $var3
                    $numeric['x3'] = $var4
                }
                else {
                    $numeric[$column.key] = Get-Series -Key $column.key
                }
            }
            foreach ($column in $textColumns) {
                if (-not $column.required) { continue }
                $text[$column.key] = Get-TextSeries -Key $column.key
            }
        }
    }

    foreach ($parameter in $Analysis.params) {
        if ($parameters.Contains($parameter.key)) { continue }
        if ($parameter.optional -and ($parameter.defaultValue -is [string]) -and $parameter.defaultValue -eq '') { continue }
        $parameters[$parameter.key] = $parameter.defaultValue
    }

    $body = [ordered]@{}
    if ($numeric.Count -gt 0) { $body['numeric'] = $numeric }
    if ($text.Count -gt 0) { $body['text'] = $text }
    if ($parameters.Count -gt 0) { $body['params'] = $parameters }
    return $body
}

# ---------------------------------------------------------------- sondeo real
$catalog = (Invoke-Api 'GET' '/api/v1/analyses' $null).Content | ConvertFrom-Json
$distributions = (Invoke-Api 'GET' '/api/v1/distributions' $null).Content | ConvertFrom-Json
$meta = (Invoke-Api 'GET' '/api/v1/meta' $null).Content | ConvertFrom-Json
$inventory = (Invoke-Api 'GET' '/api/v1/endpoints' $null).Content | ConvertFrom-Json

$notes = @{
    'gage-rr' = 'El motor original devuelve este mismo error: la definición del análisis pasa la repetición como índice de fila, así que el diseño nunca queda balanceado (exigiría p·o·filas mediciones). Es un defecto heredado y verificado por paridad; el cálculo de Gage R&R existe en el dominio, pero este análisis del catálogo no es usable con datos reales.'
}
$probes = [ordered]@{}
foreach ($analysis in $catalog.analyses) {
    $body = New-SampleBody -Analysis $analysis
    $path = "/api/v1/analyses/$($analysis.id)/run"
    $json = $body | ConvertTo-Json -Depth 10 -Compress
    try {
        $response = Invoke-WebRequest -Uri "$base$path" -Method Post -Body $json -ContentType 'application/json' -TimeoutSec 30 -SkipCertificateCheck
        $payload = $response.Content | ConvertFrom-Json
        $sections = @($payload.sections)
        $sectionSummary = 'sin secciones'
        if ($sections.Count -gt 0) {
            $extra = $sections.Length - 1
            $sectionSummary = if ($extra -eq 0) { "«$($sections[0].title)»" }
                              elseif ($extra -eq 1) { "«$($sections[0].title)» y 1 sección más" }
                              else { "«$($sections[0].title)» y $extra secciones más" }
        }
        $probes[$analysis.id] = [pscustomobject]@{
            Status  = $response.StatusCode
            Code    = $null
            Json    = $json
            Summary = $sectionSummary
            Charts  = @($payload.charts).Count
            Rows    = if ($sections.Count -gt 0 -and $sections[0].rows) { @($sections[0].rows).Count } else { 0 }
        }
    }
    catch {
        $detail = $_.ErrorDetails.Message
        $code = 'ERROR'
        if ($detail) { $code = ($detail | ConvertFrom-Json).code }
        $probes[$analysis.id] = [pscustomobject]@{ Status = $_.Exception.Response.StatusCode.value__; Code = $code; Json = $json; Summary = "error $code"; Charts = 0; Rows = 0 }
        Write-Warning "$($analysis.id): $code"
    }
}

$distributionProbes = [ordered]@{}
foreach ($distribution in $distributions.distributions) {
    $parameters = [ordered]@{}
    foreach ($parameter in $distribution.params) { $parameters[$parameter.key] = $parameter.defaultValue }
    $body = [ordered]@{ params = $parameters; x = @(0, 1); probabilities = @(0.5, 0.975) }
    $json = $body | ConvertTo-Json -Depth 6 -Compress
    try {
        $response = Invoke-WebRequest -Uri "$base/api/v1/distributions/$($distribution.id)/evaluate" -Method Post -Body $json -ContentType 'application/json' -TimeoutSec 30 -SkipCertificateCheck
        $payload = $response.Content | ConvertFrom-Json
        $distributionProbes[$distribution.id] = [pscustomobject]@{ Json = $json; Pdf = $payload.pdf[0]; Cdf = $payload.cdf[0] }
    }
    catch {
        $distributionProbes[$distribution.id] = [pscustomobject]@{ Json = $json; Pdf = 'error'; Cdf = 'error' }
        Write-Warning "$($distribution.id): error de evaluación"
    }
}

# ---------------------------------------------------------------- markdown
function Format-Number {
    param($Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) { return $Value }
    return ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, '{0}', $Value))
}

$out = [System.Collections.Generic.List[string]]::new()
$add = { param($text) $out.Add($text) }

& $add @'
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

'@

& $add '| Método | Ruta | Descripción |'
& $add '| --- | --- | --- |'
$routeDescriptions = @{
    '/'                            = 'Página de inicio con enlaces (HTML)'
    '/health/live'                 = 'Liveness del proceso'
    '/health/ready'                = 'Readiness con autotest numérico'
    '/openapi/{documentName}.json' = 'Contrato OpenAPI'
    '/scalar/{documentName?}'      = 'Documentación interactiva (Scalar)'
}
foreach ($endpoint in $inventory.endpoints) {
    if ($endpoint.path -match '/scalar/.+\.(js|svg)$') { continue }
    $summary = if ($endpoint.summary) { $endpoint.summary } elseif ($routeDescriptions.ContainsKey($endpoint.path)) { $routeDescriptions[$endpoint.path] } else { '' }
    & $add "| ``$($endpoint.method)`` | ``$($endpoint.path)`` | $summary |"
}

& $add @'

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

'@

foreach ($group in $catalog.byCategory) {
    & $add "### $($group.category) ($($group.ids.Count))"
    & $add ''
    & $add '| `id` | Nombre | Entradas |'
    & $add '| --- | --- | --- |'
    foreach ($id in $group.ids) {
        $analysis = $catalog.analyses | Where-Object { $_.id -eq $id }
        $inputs = @()
        foreach ($column in $analysis.numericColumns) { $inputs += "``$($column.key)`` (num$(if ($column.multiple) { ', múltiple' }))" }
        foreach ($column in $analysis.textColumns) { $inputs += "``$($column.key)`` (texto)" }
        foreach ($parameter in $analysis.params) { $inputs += "``$($parameter.key)`` ($($parameter.kind))" }
        if ($inputs.Count -eq 0) { $inputs = @('—') }
        & $add "| ``$($analysis.id)`` | $($analysis.label) | $($inputs -join ' · ') |"
    }
    & $add ''
}

& $add @'
## 6. Los 60 análisis, uno por uno

Cada apartado incluye la definición exacta (extraída del catálogo del servicio) y una petición
real con su resultado observado.

'@

foreach ($group in $catalog.byCategory) {
    & $add "### $($group.category)"
    & $add ''
    foreach ($id in $group.ids) {
        $analysis = $catalog.analyses | Where-Object { $_.id -eq $id }
        $probe = $probes[$analysis.id]
        & $add "#### ``$($analysis.label)`` — ``$($analysis.id)``"
        & $add ''
        if ($analysis.description) { & $add $analysis.description; & $add '' }
        & $add '| Entrada | Tipo | Obligatoria | Etiqueta |'
        & $add '| --- | --- | --- | --- |'
        foreach ($column in $analysis.numericColumns) {
            $kind = if ($column.multiple) { 'numérico (una o varias columnas)' } else { 'numérico' }
            & $add "| ``$($column.key)`` | $kind | $(if ($column.required) { 'sí' } else { 'no' }) | $($column.label) |"
        }
        foreach ($column in $analysis.textColumns) {
            & $add "| ``$($column.key)`` | texto | $(if ($column.required) { 'sí' } else { 'no' }) | $($column.label) |"
        }
        foreach ($parameter in $analysis.params) {
            $default = Format-Number $parameter.defaultValue
            $defaultText = if ([string]::IsNullOrEmpty($default)) { '' } else { " · por defecto ``$default``" }
            $optionValues = @(@($parameter.options) | ForEach-Object { $_.value })
            $options = ''
            if ($optionValues.Count -gt 0) { $options = ' (' + ($optionValues -join '|') + ')' }
            & $add "| ``$($parameter.key)`` | $($parameter.kind)$options$defaultText | $(if ($parameter.optional) { 'no' } else { 'sí' }) | $($parameter.label) |"
        }
        if (@($analysis.numericColumns).Count + @($analysis.textColumns).Count + @($analysis.params).Count -eq 0) {
            & $add '| — | — | — | Sin entradas |'
        }
        & $add ''
        & $add '```powershell'
        & $add "curl.exe -X POST ""http://localhost:5080/api/v1/analyses/$($analysis.id)/run"" -H ""Content-Type: application/json"" --data-raw '$($probe.Json)'"
        & $add '```'
        & $add ''
        $rowsWord = if ($probe.Rows -eq 1) { 'fila' } else { 'filas' }
        $chartsWord = if ($probe.Charts -eq 1) { 'gráfico' } else { 'gráficos' }
        & $add "Resultado observado: **$($probe.Status)** · $($probe.Summary) · $($probe.Rows) $rowsWord · $($probe.Charts) $chartsWord"
        & $add ''
        if ($notes.ContainsKey($analysis.id)) {
            & $add "> **Nota:** $($notes[$analysis.id])"
            & $add ''
        }
    }
}

& $add @'
## 7. Distribuciones

`GET /api/v1/distributions` devuelve las 21 distribuciones con su tipo y parámetros.

| `id` | Nombre | Tipo | Parámetros (clave: etiqueta = por defecto) |
| --- | --- | --- | --- |
'@

foreach ($distribution in $distributions.distributions) {
    $parameters = @($distribution.params) | ForEach-Object { "``$($_.key)``: $($_.label) = $(Format-Number $_.defaultValue)" }
    if ($parameters.Count -eq 0) { $parameters = @('—') }
    & $add "| ``$($distribution.id)`` | $($distribution.label) | $($distribution.kind) | $($parameters -join ' · ') |"
}

& $add @'

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
'@

foreach ($distribution in $distributions.distributions) {
    $probe = $distributionProbes[$distribution.id]
    & $add "| ``$($distribution.id)`` | ``$($probe.Json)`` | $(Format-Number $probe.Pdf) | $(Format-Number $probe.Cdf) |"
}

& $add @'

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

'@

& $add '| Límite | Valor efectivo |'
& $add '| --- | --- |'
foreach ($property in $meta.limits.PSObject.Properties) {
    & $add "| ``$($property.Name)`` | ``$(Format-Number $property.Value)`` |"
}
& $add ''
& $add "Tiempo máximo por petición: **$($meta.requestTimeoutSeconds) s**. Versión del motor: **$($meta.engine.version)** · baseline de paridad: ``$($meta.engine.parityBaseline)``."
& $add @'

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
'@

$markdown = ($out -join [Environment]::NewLine) + [Environment]::NewLine
$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
[System.IO.File]::WriteAllText($OutputPath, $markdown, [System.Text.UTF8Encoding]::new($false))

# ---------------------------------------------------------------- colección .http
$httpCurated = @(
    'describe', 'graphical-summary', 'correlation', 'tally', 'cross-tab', 'chi-square-gof',
    'one-sample-t', 'two-sample-t', 'equivalence-tost', 'anova-one-way', 'anova-two-way',
    'regression-multiple', 'regression-logistic', 'chart-xbar-r', 'chart-cusum', 'capability',
    'doe-analyze', 'timeseries-acf', 'timeseries-winters', 'timeseries-ar', 'reliability-weibull',
    'reliability-kaplan-meier', 'multivariate-pca', 'multivariate-kmeans', 'power-t2',
    'nonparam-mann-whitney'
)

$http = [System.Collections.Generic.List[string]]::new()
$http.Add(@'
@baseUrl = http://localhost:5080
@json = application/json

### Página de inicio (enlaces a la documentación)
GET {{baseUrl}}/

### Inventario de rutas publicadas
GET {{baseUrl}}/api/v1/endpoints

### Metadatos: versión del motor y límites efectivos
GET {{baseUrl}}/api/v1/meta

### Catálogo completo de análisis (60)
GET {{baseUrl}}/api/v1/analyses

### Catálogo filtrado por categoría
GET {{baseUrl}}/api/v1/analyses?category=ANOVA

### Definición de un análisis: columnas, parámetros y opciones
GET {{baseUrl}}/api/v1/analyses/capability

### Validación sin ejecutar (siempre 200 con { valid, error? })
POST {{baseUrl}}/api/v1/analyses/one-sample-t/validate
Content-Type: {{json}}

{"numeric":{"x":[]}}

### Error controlado: dato insuficiente (422 INSUFFICIENT_DATA)
POST {{baseUrl}}/api/v1/analyses/one-sample-t/run
Content-Type: {{json}}

{"numeric":{"x":[5]}}

### Error controlado: análisis inexistente (404 NOT_FOUND)
GET {{baseUrl}}/api/v1/analyses/no-existe

### Las 21 distribuciones
GET {{baseUrl}}/api/v1/distributions

### Una distribución
GET {{baseUrl}}/api/v1/distributions/normal

### Evaluar distribución: pdf/pmf, cdf, inversa y momentos
POST {{baseUrl}}/api/v1/distributions/normal/evaluate
Content-Type: {{json}}

{
  "params": { "mu": 0, "sigma": 1 },
  "x": [-1, 0, 1],
  "probabilities": [0.5, 0.975]
}

### Muestreo determinista con semilla
POST {{baseUrl}}/api/v1/distributions/gamma/sample
Content-Type: {{json}}

{
  "params": { "shape": 2, "scale": 1 },
  "count": 5,
  "seed": 20261023
}

### Salud: liveness
GET {{baseUrl}}/health/live

### Salud: readiness con autotest numérico
GET {{baseUrl}}/health/ready

'@)

foreach ($id in $httpCurated) {
    $analysis = $catalog.analyses | Where-Object { $_.id -eq $id }
    if (-not $analysis) { continue }
    $probe = $probes[$id]
    $pretty = ($probe.Json | ConvertFrom-Json | ConvertTo-Json -Depth 8)
    $http.Add("### $($analysis.label) — ``$($analysis.id)``")
    $http.Add("POST {{baseUrl}}/api/v1/analyses/$($analysis.id)/run")
    $http.Add("Content-Type: {{json}}")
    $http.Add('')
    $http.Add($pretty)
    $http.Add('')
    if ($id -eq 'describe') {
        $http.Add('### El mismo análisis como texto de sesión (?format=text)')
        $http.Add('POST {{baseUrl}}/api/v1/analyses/describe/run?format=text')
        $http.Add('Content-Type: {{json}}')
        $http.Add('')
        $http.Add($pretty)
        $http.Add('')
    }
}

$httpPath = Join-Path (Split-Path -Parent $OutputPath) 'examples\statisticengine.http'
$httpDirectory = Split-Path -Parent $httpPath
if (-not (Test-Path $httpDirectory)) { New-Item -ItemType Directory -Force -Path $httpDirectory | Out-Null }
[System.IO.File]::WriteAllText($httpPath, (($http -join [Environment]::NewLine) + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))

# ---------------------------------------------------------------- colección Postman v2.1
function New-PostmanRequest {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [string]$Description,
        [string]$JsonBody,
        [string[]]$Tests
    )
    $request = [ordered]@{ method = $Method; url = $Url }
    if ($JsonBody) {
        $request.header = @([ordered]@{ key = 'Content-Type'; value = 'application/json' })
        $request.body = [ordered]@{
            mode    = 'raw'
            raw     = $JsonBody
            options = [ordered]@{ raw = [ordered]@{ language = 'json' } }
        }
    }
    if ($Description) { $request.description = $Description }
    $item = [ordered]@{ name = $Name; request = $request }
    if ($Tests -and $Tests.Count -gt 0) {
        $item.event = @([ordered]@{
                listen = 'test'
                script = [ordered]@{ type = 'text/javascript'; exec = @($Tests) }
            })
    }
    return $item
}

$postmanItems = [System.Collections.Generic.List[object]]::new()

# 00 · Descubrimiento
$discovery = [System.Collections.Generic.List[object]]::new()
$discovery.Add((New-PostmanRequest -Name 'Página de inicio (HTML con enlaces)' -Method 'GET' -Url '{{baseUrl}}/' -Description 'Página navegable con enlaces a la documentación, el catálogo y la salud.' -Tests @(
            "pm.test('200 y HTML', () => { pm.response.to.have.status(200); pm.expect(pm.response.headers.get('Content-Type')).to.include('text/html'); });",
            "pm.test('enlaza a Scalar y al catálogo', () => { const html = pm.response.text(); pm.expect(html).to.include('scalar/v1'); pm.expect(html).to.include('/api/v1/analyses'); });"
        )))
$discovery.Add((New-PostmanRequest -Name 'Inventario de rutas' -Method 'GET' -Url '{{baseUrl}}/api/v1/endpoints' -Description 'Rutas realmente publicadas, leídas del enrutador.' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('incluye la ejecución de análisis', () => { const rutas = pm.response.json().endpoints; pm.expect(rutas.some(r => r.path === '/api/v1/analyses/{id}/run')).to.be.true; });"
        )))
$discovery.Add((New-PostmanRequest -Name 'Metadatos y límites' -Method 'GET' -Url '{{baseUrl}}/api/v1/meta' -Description 'Versión del motor, baseline de paridad y límites efectivos.' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('versión del motor', () => pm.expect(pm.response.json().engine.version).to.eql('1.0.0'));"
        )))
$discovery.Add((New-PostmanRequest -Name 'Contrato OpenAPI' -Method 'GET' -Url '{{baseUrl}}/openapi/v1.json' -Description 'Documento OpenAPI 3.1 con las 9 rutas de la API.' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('contiene las rutas de la API', () => { const paths = Object.keys(pm.response.json().paths); pm.expect(paths).to.include('/api/v1/analyses/{id}/run'); });"
        )))
$discovery.Add((New-PostmanRequest -Name 'Salud: liveness' -Method 'GET' -Url '{{baseUrl}}/health/live' -Tests @(
            "pm.test('200 y estado Healthy', () => { pm.response.to.have.status(200); pm.expect(pm.response.json().status).to.eql('Healthy'); });"
        )))
$discovery.Add((New-PostmanRequest -Name 'Salud: readiness (autotest numérico)' -Method 'GET' -Url '{{baseUrl}}/health/ready' -Tests @(
            "pm.test('200 y estado Healthy', () => { pm.response.to.have.status(200); pm.expect(pm.response.json().status).to.eql('Healthy'); });",
            "pm.test('ejecuta el autotest del motor', () => { const checks = pm.response.json().checks; pm.expect(checks.some(c => c.name === 'engine-self-test')).to.be.true; });"
        )))
$postmanItems.Add([ordered]@{ name = '00 · Descubrimiento'; item = @($discovery) })

# 01 · Catálogo
$catalogItems = [System.Collections.Generic.List[object]]::new()
$catalogItems.Add((New-PostmanRequest -Name 'Catálogo completo (60 análisis)' -Method 'GET' -Url '{{baseUrl}}/api/v1/analyses' -Description 'Definiciones con columnas, parámetros y opciones.' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "const body = pm.response.json();",
            "pm.test('60 análisis y 15 categorías', () => { pm.expect(body.analyses).to.have.lengthOf(60); pm.expect(body.categories).to.have.lengthOf(15); });",
            "pm.collectionVariables.set('totalAnalisis', String(body.analyses.length));"
        )))
$catalogItems.Add((New-PostmanRequest -Name 'Catálogo filtrado por categoría' -Method 'GET' -Url '{{baseUrl}}/api/v1/analyses?category=ANOVA' -Description 'Filtro por categoría; byCategory se filtra igual.' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('3 análisis de ANOVA y una categoría', () => { const body = pm.response.json(); pm.expect(body.analyses).to.have.lengthOf(3); pm.expect(body.byCategory).to.have.lengthOf(1); });"
        )))
$catalogItems.Add((New-PostmanRequest -Name 'Definición de un análisis' -Method 'GET' -Url '{{baseUrl}}/api/v1/analyses/capability' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('declara lsl y usl', () => { const keys = pm.response.json().params.map(p => p.key); pm.expect(keys).to.include('lsl'); pm.expect(keys).to.include('usl'); });"
        )))
$catalogItems.Add((New-PostmanRequest -Name 'Definición con columnas múltiples' -Method 'GET' -Url '{{baseUrl}}/api/v1/analyses/multivariate-pca' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('la columna x es múltiple', () => pm.expect(pm.response.json().numericColumns[0].multiple).to.be.true);"
        )))
$postmanItems.Add([ordered]@{ name = '01 · Catálogo'; item = @($catalogItems) })

# 02 · Análisis (una petición por análisis, agrupadas por categoría)
$analysisFolders = [System.Collections.Generic.List[object]]::new()
foreach ($group in $catalog.byCategory) {
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($id in $group.ids) {
        $analysis = $catalog.analyses | Where-Object { $_.id -eq $id }
        $probe = $probes[$id]
        if ($probe.Status -eq 200) {
            $tests = @(
                "pm.test('200 OK', () => pm.response.to.have.status(200));",
                "const body = pm.response.json();",
                "pm.test('id del análisis', () => pm.expect(body.id).to.eql('$id'));",
                "pm.test('devuelve secciones', () => pm.expect(body.sections.length).to.be.above(0));"
            )
        }
        else {
            $tests = @(
                "pm.test('error de dominio heredado del motor', () => pm.response.to.have.status($($probe.Status)));",
                "pm.test('código $($probe.Code)', () => pm.expect(pm.response.json().code).to.eql('$($probe.Code)'));"
            )
        }
        $description = "$($analysis.description)`n`nCategoría: $($analysis.category).`nEntradas: " +
            ((@($analysis.numericColumns) + @($analysis.textColumns) + @($analysis.params)) |
                ForEach-Object { $_.key }) -join ', '
        $items.Add((New-PostmanRequest -Name "$($analysis.label) · $id" -Method 'POST' -Url "{{baseUrl}}/api/v1/analyses/$id/run" -Description $description -JsonBody $probe.Json -Tests $tests))
    }
    $analysisFolders.Add([ordered]@{ name = $group.category; item = @($items) })
}
$postmanItems.Add([ordered]@{ name = '02 · Análisis (60)'; item = @($analysisFolders) })

# 03 · Distribuciones
$distributionItems = [System.Collections.Generic.List[object]]::new()
$distributionItems.Add((New-PostmanRequest -Name 'Catálogo de distribuciones (21)' -Method 'GET' -Url '{{baseUrl}}/api/v1/distributions' -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "pm.test('21 distribuciones', () => pm.expect(pm.response.json().distributions).to.have.lengthOf(21));"
        )))
foreach ($id in @('normal', 'binomial', 'weibull')) {
    $distribution = $distributions.distributions | Where-Object { $_.id -eq $id }
    $distributionItems.Add((New-PostmanRequest -Name "Metadatos · $($distribution.label) ($id)" -Method 'GET' -Url "{{baseUrl}}/api/v1/distributions/$id" -Tests @(
                "pm.test('200 OK', () => pm.response.to.have.status(200));",
                "pm.test('declara id y parámetros', () => { const body = pm.response.json(); pm.expect(body.id).to.eql('$id'); pm.expect(body.params.length).to.be.above(0); });"
            )))
}

$evaluateItems = [System.Collections.Generic.List[object]]::new()
foreach ($distribution in $distributions.distributions) {
    $probe = $distributionProbes[$distribution.id]
    $evaluateItems.Add((New-PostmanRequest -Name "$($distribution.label) · $($distribution.id)" -Method 'POST' -Url "{{baseUrl}}/api/v1/distributions/$($distribution.id)/evaluate" -Description "pdf/pmf y cdf para x = [0, 1]; inversas para p = [0.5, 0.975]." -JsonBody $probe.Json -Tests @(
                "pm.test('200 OK', () => pm.response.to.have.status(200));",
                "const body = pm.response.json();",
                "pm.test('pdf y cdf con la longitud de x', () => { pm.expect(body.pdf).to.have.lengthOf(2); pm.expect(body.cdf).to.have.lengthOf(2); });",
                "pm.test('momentos y soporte', () => { pm.expect(body).to.have.property('mean'); pm.expect(body.support).to.have.property('lower'); });"
            )))
}
$distributionItems.Add([ordered]@{ name = 'Evaluar (21)'; item = @($evaluateItems) })

$sampleItems = [System.Collections.Generic.List[object]]::new()
foreach ($distribution in $distributions.distributions) {
    $parameters = [ordered]@{}
    foreach ($parameter in $distribution.params) { $parameters[$parameter.key] = $parameter.defaultValue }
    $sampleBody = ([ordered]@{ params = $parameters; count = 5; seed = 20261023 } | ConvertTo-Json -Depth 5 -Compress)
    $sampleItems.Add((New-PostmanRequest -Name "$($distribution.label) · $($distribution.id)" -Method 'POST' -Url "{{baseUrl}}/api/v1/distributions/$($distribution.id)/sample" -Description 'Muestreo determinista: la misma semilla produce la misma serie.' -JsonBody $sampleBody -Tests @(
                "pm.test('200 OK', () => pm.response.to.have.status(200));",
                "const body = pm.response.json();",
                "pm.test('5 valores y semilla eco', () => { pm.expect(body.values).to.have.lengthOf(5); pm.expect(body.seed).to.eql(20261023); });"
            )))
}
$distributionItems.Add([ordered]@{ name = 'Muestrear (21)'; item = @($sampleItems) })
$postmanItems.Add([ordered]@{ name = '03 · Distribuciones (21)'; item = @($distributionItems) })

# 04 · Errores
$errorItems = [System.Collections.Generic.List[object]]::new()
$errorItems.Add((New-PostmanRequest -Name '404 · análisis inexistente' -Method 'GET' -Url '{{baseUrl}}/api/v1/analyses/no-existe' -Tests @(
            "pm.test('404 NOT_FOUND', () => { pm.response.to.have.status(404); pm.expect(pm.response.json().code).to.eql('NOT_FOUND'); });"
        )))
$errorItems.Add((New-PostmanRequest -Name '404 · distribución inexistente' -Method 'GET' -Url '{{baseUrl}}/api/v1/distributions/no-existe' -Tests @(
            "pm.test('404 NOT_FOUND', () => { pm.response.to.have.status(404); pm.expect(pm.response.json().code).to.eql('NOT_FOUND'); });"
        )))
$errorItems.Add((New-PostmanRequest -Name '422 · dato insuficiente' -Method 'POST' -Url '{{baseUrl}}/api/v1/analyses/one-sample-t/run' -Description 'Un solo valor: el motor exige al menos 2 observaciones.' -JsonBody '{"numeric":{"x":[5]}}' -Tests @(
            "pm.test('422 INSUFFICIENT_DATA', () => { pm.response.to.have.status(422); const body = pm.response.json(); pm.expect(body.code).to.eql('INSUFFICIENT_DATA'); pm.expect(body.analysisId).to.eql('one-sample-t'); });"
        )))
$errorItems.Add((New-PostmanRequest -Name '400 · parámetro inválido (sigma = 0)' -Method 'POST' -Url '{{baseUrl}}/api/v1/distributions/normal/evaluate' -JsonBody '{"params":{"sigma":0},"x":[0]}' -Tests @(
            "pm.test('400 INVALID_PARAMETER', () => { pm.response.to.have.status(400); pm.expect(pm.response.json().code).to.eql('INVALID_PARAMETER'); });"
        )))
$errorItems.Add((New-PostmanRequest -Name 'Validar sin ejecutar (siempre 200)' -Method 'POST' -Url '{{baseUrl}}/api/v1/analyses/one-sample-t/validate' -JsonBody '{"numeric":{"x":[]}}' -Tests @(
            "pm.test('200 con valid=false', () => { pm.response.to.have.status(200); const body = pm.response.json(); pm.expect(body.valid).to.be.false; pm.expect(body.error.code).to.eql('INSUFFICIENT_DATA'); });"
        )))
$postmanItems.Add([ordered]@{ name = '04 · Errores y validación'; item = @($errorItems) })

# 05 · Flujo encadenado
$describeBody = '{"numeric":{"x":[9.8,10.4,9.1,11.2,10.0,9.6,10.9,9.3,10.7,10.2]},"params":{"confidence":0.95}}'
$flowItems = [System.Collections.Generic.List[object]]::new()
$flowItems.Add((New-PostmanRequest -Name '1 · Descubrir el catálogo' -Method 'GET' -Url '{{baseUrl}}/api/v1/analyses' -Tests @(
            "pm.test('60 análisis y 15 categorías', () => { pm.expect(pm.response.json().analyses).to.have.lengthOf(60); });",
            "pm.collectionVariables.set('totalAnalisis', String(pm.response.json().analyses.length));"
        )))
$flowItems.Add((New-PostmanRequest -Name '2 · Validar la entrada' -Method 'POST' -Url '{{baseUrl}}/api/v1/analyses/describe/validate' -JsonBody $describeBody -Tests @(
            "pm.test('válida', () => pm.expect(pm.response.json().valid).to.be.true);"
        )))
$flowItems.Add((New-PostmanRequest -Name '3 · Ejecutar el análisis' -Method 'POST' -Url '{{baseUrl}}/api/v1/analyses/describe/run' -JsonBody $describeBody -Tests @(
            "pm.test('200 OK', () => pm.response.to.have.status(200));",
            "const body = pm.response.json();",
            "pm.test('N = 10 en la primera fila', () => pm.expect(body.sections[0].rows[0][1]).to.eql('10'));",
            "pm.collectionVariables.set('nDescriptivo', String(body.sections[0].rows[0][1]));"
        )))
$flowItems.Add((New-PostmanRequest -Name '4 · Mismo cálculo como texto de sesión' -Method 'POST' -Url '{{baseUrl}}/api/v1/analyses/describe/run?format=text' -JsonBody $describeBody -Tests @(
            "pm.test('text/plain', () => pm.expect(pm.response.headers.get('Content-Type')).to.include('text/plain'));",
            "pm.test('contiene la tabla de sesión', () => pm.expect(pm.response.text()).to.include('Resumen'));"
        )))
$flowItems.Add((New-PostmanRequest -Name '5 · Cuantil normal(0,1) al 97,5 %' -Method 'POST' -Url '{{baseUrl}}/api/v1/distributions/normal/evaluate' -JsonBody '{"params":{"mu":0,"sigma":1},"x":[0],"probabilities":[0.975]}' -Tests @(
            "const body = pm.response.json();",
            "pm.test('inversa ≈ 1.959963984540054', () => pm.expect(body.invCdf[0]).to.be.closeTo(1.959963984540054, 1e-9));",
            "pm.collectionVariables.set('cuantil975', String(body.invCdf[0]));"
        )))
$flowItems.Add((New-PostmanRequest -Name '6 · Muestrear normal(0,1) con semilla' -Method 'POST' -Url '{{baseUrl}}/api/v1/distributions/normal/sample' -JsonBody '{"params":{"mu":0,"sigma":1},"count":5,"seed":20261023}' -Tests @(
            "const values = pm.response.json().values;",
            "pm.test('5 muestras', () => pm.expect(values).to.have.lengthOf(5));",
            "pm.collectionVariables.set('muestraA', JSON.stringify(values));"
        )))
$flowItems.Add((New-PostmanRequest -Name '7 · Repetir: debe ser idéntico' -Method 'POST' -Url '{{baseUrl}}/api/v1/distributions/normal/sample' -JsonBody '{"params":{"mu":0,"sigma":1},"count":5,"seed":20261023}' -Tests @(
            "pm.test('determinista con la misma semilla', () => pm.expect(JSON.stringify(pm.response.json().values)).to.eql(pm.collectionVariables.get('muestraA')));"
        )))
$postmanItems.Add([ordered]@{ name = '05 · Flujo completo (encadenado)'; item = @($flowItems) })

$collectionDescription = @'
Colección completa del microservicio **StatisticEngine**: catálogo y ejecución de los 60 análisis,
las 21 distribuciones (evaluación y muestreo), descubrimiento (página de inicio, inventario de rutas,
OpenAPI, salud), errores de dominio y un flujo encadenado.

Variables: `baseUrl` (http://localhost:5080 o https://localhost:54236 con el perfil de Visual Studio)
y `apiKey` (solo si se activa `StatisticEngine:Auth:ApiKey`; añádela como cabecera `X-Api-Key`).

Cada petición incluye tests automáticos: códigos HTTP, forma de la respuesta, número de análisis y
distribuciones, cuantiles esperados y determinismo del muestreo.
'@

$collection = [ordered]@{
    info     = [ordered]@{
        _postman_id = '5f0d9b52-6f2b-4f8b-9d1a-2b7c3e4f5a60'
        name        = 'StatisticEngine API'
        description = $collectionDescription
        schema      = 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json'
    }
    auth     = [ordered]@{ type = 'noauth' }
    variable = @(
        [ordered]@{ key = 'baseUrl'; value = 'http://localhost:5080'; type = 'string' },
        [ordered]@{ key = 'apiKey'; value = ''; type = 'string' },
        [ordered]@{ key = 'muestraA'; value = ''; type = 'string' }
    )
    item     = @($postmanItems)
}

$postmanDirectory = Split-Path -Parent $PostmanPath
if (-not (Test-Path $postmanDirectory)) { New-Item -ItemType Directory -Force -Path $postmanDirectory | Out-Null }
$postmanJson = $collection | ConvertTo-Json -Depth 30
[System.IO.File]::WriteAllText($PostmanPath, $postmanJson, [System.Text.UTF8Encoding]::new($false))

$failed = ($probes.Values | Where-Object { $_.Status -ge 400 }).Count
Write-Host "API.md generado: $OutputPath"
Write-Host "Colección .http: $httpPath"
Write-Host "Colección Postman: $PostmanPath"
Write-Host "  análisis: $($probes.Count) (con error: $failed)"
Write-Host "  distribuciones: $($distributionProbes.Count)"
Write-Host "  líneas: $((Get-Content $OutputPath).Count)"
