using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StatisticEngine.Api.Infrastructure;
using Xunit;

namespace StatisticEngine.Api.Tests;

/// <summary>Host de pruebas con la configuración de <c>appsettings.Testing.json</c>.</summary>
public sealed class StatisticEngineFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}

/// <summary>Factoría con autenticación por clave de API activada.</summary>
public sealed class SecuredFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
            services.PostConfigure<StatisticEngineOptions>(options => options.Auth.ApiKey = "clave-secreta"));
    }
}

public class ApiContractTests : IClassFixture<StatisticEngineFactory>
{
    private readonly HttpClient _client;

    public ApiContractTests(StatisticEngineFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task ElCatalogoExponeLosSesentaAnalisis()
    {
        using var document = await GetJson("/api/v1/analyses");
        var root = document.RootElement;
        Assert.Equal(60, root.GetProperty("analyses").GetArrayLength());
        Assert.Equal(15, root.GetProperty("categories").GetArrayLength());
        Assert.Equal("StatisticEngine", root.GetProperty("engine").GetProperty("name").GetString());
        Assert.Equal("1.0.0", root.GetProperty("engine").GetProperty("version").GetString());
    }

    [Fact]
    public async Task ElCatalogoFiltraPorCategoriaDeFormaCoherente()
    {
        using var document = await GetJson("/api/v1/analyses?category=ANOVA");
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("analyses").GetArrayLength());
        Assert.Equal(1, root.GetProperty("byCategory").GetArrayLength());
        Assert.Equal("ANOVA", root.GetProperty("byCategory")[0].GetProperty("category").GetString());
    }

    [Fact]
    public async Task EjecutaUnaDescriptiva()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/analyses/describe/run",
            new { numeric = new { x = new[] { 1, 2, 3, 4, 5 } }, @params = new { confidence = 0.95 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("describe", root.GetProperty("id").GetString());
        var section = root.GetProperty("sections")[0];
        Assert.Equal("table", section.GetProperty("kind").GetString());
        Assert.Equal("Resumen", section.GetProperty("title").GetString());
        Assert.Equal("x", section.GetProperty("rows")[0][0].GetString());
        Assert.Equal("5", section.GetProperty("rows")[0][1].GetString());
    }

    [Fact]
    public async Task DevuelveElTextoDeSesionCuandoSePideText()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/analyses/describe/run?format=text",
            new { numeric = new { x = new[] { 1, 2, 3 } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("Estadísticas descriptivas", text, StringComparison.Ordinal);
        Assert.Contains("─ Resumen", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnAnalisisDesconocidoDevuelve404ConCodigo()
    {
        var response = await _client.GetAsync("/api/v1/analyses/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnDatoInsuficienteDevuelve422ConElCodigoDelMotor()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/analyses/one-sample-t/run",
            new { numeric = new { x = new[] { 1 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("INSUFFICIENT_DATA", root.GetProperty("code").GetString());
        Assert.Equal("one-sample-t", root.GetProperty("analysisId").GetString());
        Assert.Equal("Se necesitan al menos 2 observaciones.", root.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnExcesoDeParametrosDevuelve413()
    {
        var parameters = Enumerable.Range(0, 6).ToDictionary(index => $"p{index}", index => (object)index);
        var response = await _client.PostAsJsonAsync(
            "/api/v1/analyses/describe/run",
            new { numeric = new { x = new[] { 1, 2, 3 } }, @params = parameters });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("API_PAYLOAD_TOO_LARGE", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LaValidacionInformaElMotivoSinMarcarErrorHttp()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/analyses/one-sample-t/validate",
            new { numeric = new { x = Array.Empty<double>() } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.Equal("INSUFFICIENT_DATA", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task LasDistribucionesSeListanYSeEvaluan()
    {
        using var list = await GetJson("/api/v1/distributions");
        Assert.Equal(21, list.RootElement.GetProperty("distributions").GetArrayLength());

        var evaluate = await _client.PostAsJsonAsync(
            "/api/v1/distributions/normal/evaluate",
            new
            {
                @params = new { mu = 0, sigma = 1 },
                x = new[] { -1, 0, 1 },
                probabilities = new[] { 0.975 }
            });
        Assert.Equal(HttpStatusCode.OK, evaluate.StatusCode);

        using var document = JsonDocument.Parse(await evaluate.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(0.5, root.GetProperty("cdf")[1].GetDouble(), 12);
        Assert.Equal(0.3989422804014327, root.GetProperty("pdf")[1].GetDouble(), 12);
        Assert.Equal(1.959963984540054, root.GetProperty("invCdf")[0].GetDouble(), 9);
        Assert.Equal(0, root.GetProperty("mean").GetDouble(), 12);
    }

    [Fact]
    public async Task ElMuestreoEsDeterministaConSemilla()
    {
        var payload = new { count = 5, seed = 20261023, @params = new { mu = 0, sigma = 1 } };
        var first = await _client.PostAsJsonAsync("/api/v1/distributions/normal/sample", payload);
        var second = await _client.PostAsJsonAsync("/api/v1/distributions/normal/sample", payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);

        using var document = JsonDocument.Parse(firstBody);
        Assert.Equal(5, document.RootElement.GetProperty("values").GetArrayLength());
    }

    [Fact]
    public async Task LosProbesDeSaludRespondenJson()
    {
        using var live = await GetJson("/health/live");
        Assert.Equal("Healthy", live.RootElement.GetProperty("status").GetString());

        using var ready = await GetJson("/health/ready");
        Assert.Equal("Healthy", ready.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            ready.RootElement.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("name").GetString() == "engine-self-test");
    }

    [Fact]
    public async Task LosMetadatosReflejanLaConfiguracionEfectiva()
    {
        using var document = await GetJson("/api/v1/meta");
        var root = document.RootElement;
        Assert.False(root.GetProperty("limits").GetProperty("rateLimitEnabled").GetBoolean());
        Assert.Equal(100, root.GetProperty("limits").GetProperty("maxNumericValues").GetInt32());
        Assert.True(root.GetProperty("uptimeSeconds").GetDouble() >= 0);
    }

    [Fact]
    public async Task TodasLasRespuestasLlevanLaVersionDelMotor()
    {
        var response = await _client.GetAsync("/api/v1/meta");
        Assert.True(response.Headers.TryGetValues("X-StatisticEngine-Version", out var values));
        Assert.Equal("1.0.0", values!.Single());
    }

    [Fact]
    public async Task ElDocumentoOpenApiIncluyeLasRutas()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/analyses", text, StringComparison.Ordinal);
        Assert.Contains("/api/v1/distributions/{id}/evaluate", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaRutaDesconocidaDevuelveProblemDetailsConCodigo()
    {
        var response = await _client.GetAsync("/api/v1/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LaRaizDevuelveUnaPaginaNavegable()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("scalar/v1", html, StringComparison.Ordinal);
        Assert.Contains("openapi/v1.json", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/analyses", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElInventarioEnumeraLasRutasReales()
    {
        using var document = await GetJson("/api/v1/endpoints");
        var endpoints = document.RootElement.GetProperty("endpoints").EnumerateArray().ToArray();

        Assert.Contains(endpoints, endpoint =>
            endpoint.GetProperty("method").GetString() == "POST"
            && endpoint.GetProperty("path").GetString() == "/api/v1/analyses/{id}/run");
        Assert.Contains(endpoints, endpoint =>
            endpoint.GetProperty("method").GetString() == "POST"
            && endpoint.GetProperty("path").GetString() == "/api/v1/distributions/{id}/evaluate");
        Assert.Contains(endpoints, endpoint =>
            endpoint.GetProperty("path").GetString() == "/api/v1/analyses");
        Assert.True(endpoints.Length >= 15, $"Se esperaban al menos 15 rutas y hay {endpoints.Length}.");
    }

    [Fact]
    public async Task LaDocumentacionInteractivaEstaDisponible()
    {
        var response = await _client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonDocument> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}

public class ApiSecurityTests : IClassFixture<SecuredFactory>
{
    private readonly HttpClient _client;

    public ApiSecurityTests(SecuredFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task SinClaveDevuelve401()
    {
        var response = await _client.GetAsync("/api/v1/meta");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("API_UNAUTHORIZED", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConClaveValidaResponde()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        request.Headers.Add("X-Api-Key", "clave-secreta");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LosProbesDeSaludNoRequierenClave()
    {
        var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
