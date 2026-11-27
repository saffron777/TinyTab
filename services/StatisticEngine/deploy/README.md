# Despliegue de StatisticEngine

## Lo primero: Apache no ejecuta ASP.NET Core

`httpd` no tiene módulo para .NET (el viejo `mod_aspdotnet` solo soportaba .NET Framework y está
muerto). La forma correcta de publicar este servicio "en Apache" es:

```
cliente ──HTTPS──▶ Apache (TLS, límites, logs) ──HTTP──▶ Kestrel 127.0.0.1:5080 (StatisticEngine)
```

Apache actúa de **proxy inverso** con `mod_proxy` + `mod_proxy_http`. El servicio escucha **solo en
loopback**, de modo que la única entrada pública es Apache.

## Topologías

| Topología | Cuándo | Archivo |
| --- | --- | --- |
| **A. Subdominio** (recomendada) | `https://stats.tudominio.com/` → API completa, Scalar y OpenAPI | [apache/statisticengine.conf](apache/statisticengine.conf) |
| **B. Subruta** (un solo vhost ya usado) | `https://app.tudominio.com/statisticengine/api/...` | [apache/statisticengine-subruta.conf](apache/statisticengine-subruta.conf) |
| **C. Contenedores** | Host Linux con Docker: Apache + servicio en redes separadas | [docker/](docker/) (variables en [docker/.env.example](docker/.env.example)) |

> **Aviso sobre la topología B:** el servicio monta Swagger/Scalar y el documento OpenAPI en rutas
> **absolutas** (`/scalar/v1`, `/openapi/v1.json`). Colgados de una subruta no funcionan sin
> `UsePathBase`, que el servicio no configura hoy. Por eso B expone **solo la API y los probes**;
> para la UI interactiva usa el subdominio (A) o consúltala en el propio servidor por loopback.
> Si necesitas Scalar bajo subruta, es un cambio pequeño en `Program.cs` (`app.UsePathBase(...)`);
> pídemelo y lo añado con configuración `StatisticEngine:PathBase`.

## Configuración obligatoria del servicio detrás de Apache

Variables de entorno (o `appsettings.Production.json`):

| Variable | Valor | Por qué |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Desactiva logs verbosos y Swagger de desarrollo (aquí Scalar está siempre, pero sin detalle de errores) |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` | Solo loopback: nadie salta a Apache |
| `StatisticEngine__Proxy__Enabled` | `true` | **Crítico.** Sin esto el rate limit agrupa a todos los clientes bajo la IP de Apache y `X-Forwarded-Proto` se ignora |
| `StatisticEngine__Auth__ApiKey` | cadena larga y aleatoria | Apache pasa la cabecera `X-Api-Key` tal cual; sin clave, cualquiera que alcance el puerto entra |
| `StatisticEngine__ApiExplorer__Enabled` | `false` (opcional) | No publicar Scalar/OpenAPI en producción |
| `StatisticEngine__Limits__HeavyRequestTimeoutSeconds` | `300` (por defecto) | Los análisis y las operaciones de datos usan el perfil de timeout "heavy" |

**Tiempos:** análisis y `/api/v1/data/*` pueden tardar hasta 300 s. Apache debe esperar más:

- `ProxyTimeout 330` como mínimo (sube ambos valores si amplías el timeout del servicio).
- `Timeout 330` a nivel de servidor si tu Apache usa el valor por defecto de 60 s.

**Cuerpos:** Kestrel acepta hasta 32 MiB (`Limits:MaxRequestBodyBytes`). Coherente con
`LimitRequestBody 33554432` en Apache. Si subes uno, sube el otro.

## Windows (Apache + servicio)

1. **Publicar**

   ```powershell
   cd services\StatisticEngine
   dotnet publish src\StatisticEngine.Api\StatisticEngine.Api.csproj -c Release -o C:\Services\StatisticEngine
   ```

2. **Instalar como servicio** (requiere [NSSM](https://nssm.cc/) en el PATH y consola de administrador):

   ```powershell
   .\deploy\windows\install-service.ps1 -Action install -ApiKey "PON-AQUI-UNA-CLAVE-LARGA"
   ```

   Alternativa sin NSSM: publicar como servicio de Windows añadiendo
   `Microsoft.Extensions.Hosting.WindowsServices` y `.UseWindowsService()` (cambio de código), o
   envolver el `.exe` con WinSW.

3. **Habilitar módulos de Apache** en `httpd.conf` (descomenta o añade):

   ```apache
   LoadModule proxy_module        modules/mod_proxy.so
   LoadModule proxy_http_module   modules/mod_proxy_http.so
   LoadModule headers_module      modules/mod_headers.so
   LoadModule ssl_module          modules/mod_ssl.so
   LoadModule rewrite_module      modules/mod_rewrite.so
   LoadModule deflate_module      modules/mod_deflate.so
   LoadModule filter_module       modules/mod_filter.so
   ```

4. **Incluir el vhost**: copia `deploy\apache\statisticengine.conf` a
   `C:\Apache24\conf\extra\` y añade al final de `httpd.conf`:

   ```apache
   Include conf/extra/statisticengine.conf
   ```

   Ajusta `ServerName`, rutas de certificado y, si el servicio no está en el mismo host,
   `127.0.0.1:5080`. Valida y recarga:

   ```powershell
   C:\Apache24\bin\httpd.exe -t
   Restart-Service Apache2.4
   ```

## Linux (Apache + systemd)

```bash
# 1. Publicar
dotnet publish src/StatisticEngine.Api/StatisticEngine.Api.csproj -c Release -o /opt/statisticengine

# 2. Servicio
sudo cp deploy/linux/statisticengine.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now statisticengine

# 3. Apache
sudo a2enmod proxy proxy_http headers ssl rewrite deflate
sudo cp deploy/apache/statisticengine.conf /etc/apache2/sites-available/
sudo a2ensite statisticengine && sudo apache2ctl configtest && sudo systemctl reload apache2
```

El usuario del servicio (`statisticengine`) no necesita escribir nada en disco: el proceso es
stateless. El unit de systemd ya aplica `ProtectSystem=strict`, `PrivateTmp` y `NoNewPrivileges`.

## Verificación

```powershell
# desde el propio servidor: el servicio está vivo y ve el proxy
curl.exe http://127.0.0.1:5080/health/ready

# a través de Apache (topología A)
curl.exe -H "X-Api-Key: TU_CLAVE" https://stats.tudominio.com/health/ready
curl.exe -H "X-Api-Key: TU_CLAVE" https://stats.tudominio.com/api/v1/analyses
curl.exe -H "X-Api-Key: TU_CLAVE" https://stats.tudominio.com/api/v1/data

# a través de Apache (topología B)
curl.exe -H "X-Api-Key: TU_CLAVE" https://app.tudominio.com/statisticengine/api/v1/analyses
```

## Estado de verificación

Comprobado con Docker Desktop (contenedores Linux) e `httpd:2.4`:

- **`docker build` de la imagen**: el `Dockerfile` copia `.editorconfig`; sin él, `TreatWarningsAsErrors`
  convierte CA1859 en error dentro del contenedor y el build falla (nos pasó y está corregido).
- **Contenedor del servicio**: `/health/*` sin clave, `401` sin `X-Api-Key`, `200` con ella;
  162 análisis, 23 operaciones de datos y 21 distribuciones; análisis y `evaluate` correctos.
- **Stack Apache + servicio**: `httpd -t` → `Syntax OK`; `/health/ready`, catálogo, Scalar,
  OpenAPI, `404` en `problem+json`, ejecución de un análisis y de una operación de datos
  (`data-standardize`, 106 ms) y líneas de acceso en `docker compose logs apache`.
- **Rate limit por IP real detrás del proxy**: con `Proxy.Enabled=true`, 60 peticiones de la
  IP A → `200` y las siguientes `429`; una IP B distinta → `200`. Sin el flag todas comparten la
  IP de Apache.
- **`.env` autoload**: el compose arranca solo con `deploy/docker/.env` y falla a propósito si
  falta `STATISTICENGINE_API_KEY`.
- **Healthchecks**: el servicio reporta `healthy` y Apache arranca con `condition: service_healthy`.

No verificable en la máquina de desarrollo: TLS real (los vhosts ya traen `X-Forwarded-Proto`),
`install` con NSSM (requiere administrador) y la unidad systemd (host Linux).

## Problemas típicos

| Síntoma | Causa | Solución |
| --- | --- | --- |
| `502 Bad Gateway` | Kestrel caído o escuchando en otra dirección/puerto | `Get-NetTCPConnection -LocalPort 5080`; revisar logs del servicio |
| Todo el mundo recibe `429` a la vez | `Proxy.Enabled=false`: el rate limit ve solo la IP de Apache | `StatisticEngine__Proxy__Enabled=true` y reiniciar |
| `401 API_UNAUTHORIZED` | Clave configurada y no enviada | Enviar `X-Api-Key`; Apache no debe borrar cabeceras (`RequestHeader unset` cuidado) |
| `404` en `/scalar/v1` bajo subruta | Rutas absolutas de Scalar/OpenAPI | Usar subdominio (A) o `UsePathBase` |
| `413` | Cuerpo mayor que Apache o Kestrel | `LimitRequestBody` y `MaxRequestBodyBytes` |
| `504` en análisis grandes | `ProxyTimeout` menor que el timeout `heavy` | `ProxyTimeout 330` o más |
| `Connection reset` en subidas grandes | `mod_reqtimeout` | `RequestReadTimeout body=60` |
| HTTPS "mixto" o redirecciones a `http://` | Falta `X-Forwarded-Proto` | `RequestHeader set X-Forwarded-Proto "https"` |

## Checklist de seguridad

- El servicio escucha **solo** en `127.0.0.1` (o en la red interna de Docker, sin publicar puerto).
- `StatisticEngine__Auth__ApiKey` con valor largo; rota la clave reiniciando el servicio.
- TLS termina en Apache; `ProxyPreserveHost On` y `ProxyRequests Off`.
- `StatisticEngine__ApiExplorer__Enabled=false` si no quieres publicar la UI interactiva.
- Rate limit activo (`RateLimit__Enabled=true`, por defecto) con `Proxy.Enabled=true`.
- Logs en Apache (`ErrorLog`/`CustomLog`) y en el servicio (stdout capturado por NSSM/systemd).
