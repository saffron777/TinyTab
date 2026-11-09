namespace StatisticEngine.Api.Endpoints;

/// <summary>
/// Página de inicio del servicio. Es HTML estático y autocontenido (sin CDN ni JavaScript):
/// sirve para descubrir los endpoints cuando se abre la raíz del servicio en el navegador,
/// que de otro modo devolvería 404.
/// </summary>
public static class LandingPage
{
    public static string Render(string version, bool scalarEnabled)
    {
        var scalarCard = scalarEnabled
            ? "<a class=\"card\" href=\"scalar/v1\"><strong>Documentación interactiva</strong>"
              + "<span>Scalar UI: probar cada endpoint</span></a>"
            : string.Empty;

        return $$$"""
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>StatisticEngine {{{version}}}</title>
<style>
  :root { color-scheme: light dark; --bg: #f7f7f8; --fg: #1c1c1e; --muted: #6b6b70; --card: #ffffff; --line: #e3e3e6; --accent: #0b6bcb; }
  @media (prefers-color-scheme: dark) { :root { --bg: #161618; --fg: #ececef; --muted: #a0a0a8; --card: #1f1f22; --line: #2f2f34; --accent: #6aa9ff; } }
  * { box-sizing: border-box; }
  body { margin: 0; padding: 2.5rem 1.25rem 4rem; background: var(--bg); color: var(--fg);
         font: 16px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
  main { max-width: 62rem; margin: 0 auto; }
  h1 { font-size: 1.9rem; margin: 0 0 .25rem; letter-spacing: -.02em; }
  .sub { color: var(--muted); margin: 0 0 2rem; }
  h2 { font-size: 1.05rem; text-transform: uppercase; letter-spacing: .08em; color: var(--muted);
       margin: 2.25rem 0 .75rem; font-weight: 600; }
  .grid { display: grid; gap: .75rem; grid-template-columns: repeat(auto-fit, minmax(15rem, 1fr)); }
  a.card { display: block; padding: .9rem 1rem; background: var(--card); border: 1px solid var(--line);
           border-radius: .65rem; text-decoration: none; color: inherit; }
  a.card:hover { border-color: var(--accent); }
  a.card strong { display: block; color: var(--accent); font-size: .98rem; }
  a.card span { color: var(--muted); font-size: .85rem; }
  table { width: 100%; border-collapse: collapse; background: var(--card); border: 1px solid var(--line);
          border-radius: .65rem; overflow: hidden; font-size: .9rem; }
  th, td { text-align: left; padding: .55rem .75rem; border-bottom: 1px solid var(--line); vertical-align: top; }
  th { color: var(--muted); font-weight: 600; font-size: .8rem; text-transform: uppercase; letter-spacing: .05em; }
  tr:last-child td { border-bottom: 0; }
  code, pre { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: .85rem; }
  pre { background: var(--card); border: 1px solid var(--line); border-radius: .65rem; padding: .9rem 1rem;
        overflow-x: auto; margin: 0; }
  .method { font-weight: 700; color: var(--accent); }
  footer { margin-top: 2.5rem; color: var(--muted); font-size: .82rem; }
</style>
</head>
<body>
<main>
  <h1>StatisticEngine <span style="color: var(--muted); font-weight: 400">v{{version}}</span></h1>
  <p class="sub">Motor estadístico de TinyTab como microservicio: 21 distribuciones y 60 análisis con
  paridad verificada. API base <code>/api/v1</code>.</p>

  <h2>Explorar</h2>
  <div class="grid">
    {{{scalarCard}}}
    <a class="card" href="openapi/v1.json"><strong>OpenAPI 3.1 (JSON)</strong><span>Contrato completo para clientes</span></a>
    <a class="card" href="api/v1/endpoints"><strong>Inventario de endpoints</strong><span>Rutas reales publicadas por el servicio</span></a>
    <a class="card" href="api/v1/analyses"><strong>Catálogo de análisis</strong><span>60 análisis con columnas y parámetros</span></a>
    <a class="card" href="api/v1/distributions"><strong>Distribuciones</strong><span>21 distribuciones y sus parámetros</span></a>
    <a class="card" href="api/v1/meta"><strong>Metadatos</strong><span>Versión, límites y tiempo en marcha</span></a>
    <a class="card" href="health/ready"><strong>Estado</strong><span>Readiness con autotest numérico</span></a>
    <a class="card" href="health/live"><strong>Vivo</strong><span>Liveness del proceso</span></a>
  </div>

  <h2>Endpoints</h2>
  <table>
    <thead><tr><th>Método</th><th>Ruta</th><th>Qué hace</th></tr></thead>
    <tbody>
      <tr><td class="method">GET</td><td><code>/api/v1/analyses</code></td><td>Catálogo completo; filtrable con <code>?category=</code></td></tr>
      <tr><td class="method">GET</td><td><code>/api/v1/analyses/{id}</code></td><td>Definición de un análisis: columnas, parámetros y descripción</td></tr>
      <tr><td class="method">POST</td><td><code>/api/v1/analyses/{id}/run</code></td><td>Ejecuta el análisis; <code>?format=text</code> devuelve la sesión en texto</td></tr>
      <tr><td class="method">POST</td><td><code>/api/v1/analyses/{id}/validate</code></td><td>Valida la entrada sin ejecutar: <code>200 { valid, error? }</code></td></tr>
      <tr><td class="method">GET</td><td><code>/api/v1/distributions</code></td><td>Las 21 distribuciones con sus parámetros</td></tr>
      <tr><td class="method">GET</td><td><code>/api/v1/distributions/{id}</code></td><td>Una distribución</td></tr>
      <tr><td class="method">POST</td><td><code>/api/v1/distributions/{id}/evaluate</code></td><td>PDF/PMF, CDF, inversa, media, desviación y soporte</td></tr>
      <tr><td class="method">POST</td><td><code>/api/v1/distributions/{id}/sample</code></td><td>Muestreo determinista con semilla</td></tr>
      <tr><td class="method">GET</td><td><code>/api/v1/meta</code></td><td>Versión, baseline de paridad y límites efectivos</td></tr>
      <tr><td class="method">GET</td><td><code>/api/v1/endpoints</code></td><td>Inventario de rutas vivas (esta misma tabla, siempre al día)</td></tr>
      <tr><td class="method">GET</td><td><code>/health/live</code> · <code>/health/ready</code></td><td>Probes de salud en JSON</td></tr>
    </tbody>
  </table>

  <h2>Primera petición</h2>
  <pre><code>curl -X POST http://localhost:5080/api/v1/analyses/describe/run \
  -H "Content-Type: application/json" \
  -d '{"numeric":{"x":[1,2,3,4,5]},"params":{"confidence":0.95}}'</code></pre>

  <footer>
    Guía completa con ejemplos de todos los análisis y distribuciones: <code>services/StatisticEngine/API.md</code>.
    Errores en <code>application/problem+json</code> con extensión <code>code</code>.
    Cabecera <code>X-StatisticEngine-Version</code> en todas las respuestas.
  </footer>
</main>
</body>
</html>
""";
    }
}
