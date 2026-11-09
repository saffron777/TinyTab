# Vectores dorados del motor estadístico

Generador de los vectores de referencia que usa `StatisticEngine.Domain.Tests` para verificar la
paridad 1:1 con el motor TypeScript de TinyTab.

## Qué genera

Un JSON por módulo en `tests/StatisticEngine.Domain.Tests/Parity/vectors/`:

| Archivo | Origen TypeScript |
| --- | --- |
| `numeric.json` | `src/domain/stats/numeric.ts` (incluye `fmt`/`fmtP`) |
| `rng.json` | `src/domain/stats/random.ts` |
| `linalg.json` | `src/domain/stats/linalg.ts` |
| `descriptive.json` | `src/domain/stats/descriptive.ts` |
| `tables.json` | `src/domain/stats/tables.ts` |
| `tests.json` | `src/domain/stats/tests.ts` |
| `anova.json` | `src/domain/stats/anova.ts` |
| `regression.json` | `src/domain/stats/regression.ts` |
| `spc.json` | `src/domain/stats/spc.ts` |
| `msa.json` | `src/domain/stats/msa.ts` |
| `doe.json` | `src/domain/stats/doe.ts` |
| `timeseries.json` | `src/domain/stats/timeseries.ts` |
| `reliability.json` | `src/domain/stats/reliability.ts` |
| `power.json` | `src/domain/stats/power.ts` |
| `multivariate.json` | `src/domain/stats/multivariate.ts` |
| `distributions.json` | `src/domain/stats/distributions.ts` |
| `catalog.json` | `src/domain/analysis/index.ts` + registros |
| `analyses.json` | los 60 análisis con la entrada de `tests/unit/analysis-smoke.test.ts` |
| `text-format.json` | `formatAnalysisResult` y `sectionToPlainText` |

## Regeneración

```powershell
npx vitest run --config services/StatisticEngine/tools/golden/vitest.config.ts
```

No modifica el código de TinyTab: es una configuración de Vitest aislada que importa el dominio
existente con los alias `@domain`/`@application`.

## Formato

```jsonc
{
  "module": "spc",
  "engine": { "name": "tinytab-statistical-software", "version": "0.1.0", "parityBaseline": "tinytab@0.1.0/src/domain" },
  "encoding": "NaN|Infinity|-Infinity|-0 -> {\"$num\":\"...\"}; ...",
  "functions": {
    "processCapability": [
      { "args": [[10, 10.2], 9.5, 10.5], "expected": { "ok": true, "value": { "mean": 10.1, "cp": 0.9 } } }
    ]
  }
}
```

Reglas de codificación:

- `NaN`, `Infinity`, `-Infinity` y `-0` se codifican como `{ "$num": "NaN" }`, etc. (JSON no puede
  representarlos y `JSON.stringify` los convertiría en `null`).
- `undefined` → `{ "$undefined": true }`; las propiedades `undefined` se omiten.
- `Map`/`Set` → `{ "$map": [[clave, valor], …] }` / `{ "$set": [...] }` (sólo en argumentos).
- Los argumentos siguen el orden de la firma TypeScript. Excepciones documentadas:
  - `Rng.*`: el primer argumento es la semilla y el último la cantidad de extracciones.
  - `describeByGroup`: el primer argumento es una lista de pares `[etiqueta, valores]`.
- Resultados del dominio: `{ "ok": true, "value": … }` o `{ "ok": false, "error": { "code", "message" } }`.
  Los `Result<void>` producen `{ "ok": true }` sin `value`.

## Uso desde C#

```csharp
var registry = new Dictionary<string, Func<GoldenCase, object?>>
{
    ["mean"] = c => NumericMath.Mean(GoldenReader.Numbers(c.Args[0])),
    ["processCapability"] = c => ResultDto.From(ProcessCapability(
        GoldenReader.Numbers(c.Args[0]), GoldenReader.Number(c.Args[1]), GoldenReader.Number(c.Args[2])))
};

GoldenSuite.Run("spc", registry, 1e-12);            // todos los casos del módulo
GoldenSuite.RunSelected("spc", registry, 1e-12, "histogram"); // sólo algunas funciones
```

`GoldenSuite` falla si el vector contiene una función sin implementación registrada, de modo que la
cobertura es obligatoria y verificable.

## Tolerancias

| Tipo de resultado | Tolerancia |
| --- | --- |
| Textos, claves y estructuras | exacta |
| Estado entero del RNG (`nextUint32`, `next`, `binomial`, `poisson`) | exacta |
| Funciones analíticas, cuantiles, distribuciones | `1e-12` relativa |
| Ajustes iterativos (Weibull MLE, IRLS, k-means, AR) | `1e-9` relativa |

La tolerancia nunca se usa para "hacer pasar" una diferencia de estructura: los nombres de propiedad
y los códigos de error deben coincidir literalmente.
