# ADR 0005 — Microservicio StatisticEngine en C# / .NET 10

- Estado: aceptado
- Fecha: 2026-11-09

## Contexto

TinyTab tiene todo el cálculo en `src/domain` (TypeScript puro, sin dependencias estadísticas
externas): 16 módulos, 21 distribuciones y un catálogo de 60 análisis. Se necesita exponer ese
motor como servicio HTTP para otros clientes (automatización, integraciones, futuras interfaces)
sin duplicar la lógica ni arrastrar Electron/Node al servidor.

El motor es el activo crítico: cualquier divergencia numérica silenciosa invalidaría los
resultados. La referencia de verdad es la implementación TypeScript existente, incluidos sus
defectos conocidos y documentados.

## Decisión

1. **Port nativo a C# / ASP.NET Core 10 (`net10.0`)**, no un proxy sobre Node. El servicio es
   autónomo: un único proceso, imagen de contenedor sin runtime de JavaScript.
2. **Arquitectura limpia de tres proyectos** que espeja la del escritorio:
   `StatisticEngine.Domain` (cálculo puro) → `StatisticEngine.Application` (casos de uso y puertos)
   → `StatisticEngine.Api` (host, contratos y middleware). El dominio no conoce HTTP.
3. **Paridad 1:1 verificada con vectores dorados** generados ejecutando el motor TypeScript
   (`services/StatisticEngine/tools/golden`): 16 módulos, 21 distribuciones, 60 análisis, catálogo
   y formato de texto. Los defectos del motor se reproducen, no se corrigen.
4. **Semántica numérica de JavaScript emulada donde importa**: `NumberFormat` reimplementa
   `toFixed`/`toExponential`/`String(number)` con expansión decimal exacta; `Xoshiro128` es
   bit-exacto; `Result<T>` reemplaza a las excepciones para errores de usuario.
5. **Microservicio operable**: API versionada, OpenAPI, probes de salud con autotest numérico,
   límites de carga, timeouts, rate limit, clave de API opcional, métricas por `ActivitySource` y
   configuración por entorno. Sin base de datos ni estado.

## Alternativas descartadas

- **Proxy al motor TypeScript** (sidecar Node): habría duplicado el runtime, añadido latencia y
  acoplado el servicio a Node; el motor es puro y no necesita el ecosistema.
- **Reimplementar con bibliotecas estadísticas de terceros** (MathNet, ALGLIB…): cambiaría
  resultados en la última cifra, imposibilitaría la paridad y añadiría dependencias con licencias
  distintas de GPL-3.0-or-later.
- **Copiar el catálogo a mano sin vectores**: sin verificación automática, cualquier refactor
  futuro del motor TypeScript rompería el servicio en silencio.

## Consecuencias

- El servicio se despliega como una única imagen no root (`:8080`) y arranca en menos de un
  segundo; no requiere red ni base de datos.
- Las pruebas de dominio (29) y de API (19) se ejecutan en menos de un segundo y detectan
  cualquier divergencia de cálculo, de nombres JSON, de mensajes de error o de formato.
- **Mantenimiento:** si el motor TypeScript cambia, hay que regenerar los vectores y ajustar el
  port. `PARITY-NOTES.md` documenta el procedimiento y las desviaciones aceptadas.
- La rotación `varimax` del motor no converge (iteración caótica); se reproduce el algoritmo y se
  verifican invariantes de rotación en lugar de valores exactos. Es la única familia donde la
  igualdad numérica no es alcanzable por diseño.
- El contrato HTTP es la nueva superficie pública del motor: obliga a versionar (`/api/v1`), a
  documentar los límites y a mantener `ProblemDetails` como formato único de error.
