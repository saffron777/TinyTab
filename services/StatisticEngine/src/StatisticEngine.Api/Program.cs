using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using StatisticEngine.Api.Endpoints;
using StatisticEngine.Api.Infrastructure;
using StatisticEngine.Application;
using StatisticEngine.Application.Ports;
using StatisticEngine.Application.UseCases;

var builder = WebApplication.CreateBuilder(args);

var engineOptions = builder.Configuration
    .GetSection(StatisticEngineOptions.SectionName)
    .Get<StatisticEngineOptions>() ?? new StatisticEngineOptions();

builder.Services
    .Configure<StatisticEngineOptions>(builder.Configuration.GetSection(StatisticEngineOptions.SectionName));

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = engineOptions.Limits.MaxRequestBodyBytes;
});

builder.Services.ConfigureHttpJsonOptions(options => JsonConfiguration.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IAnalysisRunner, AnalysisService>();
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddHealthChecks().AddCheck<EngineSelfTestHealthCheck>("engine-self-test", tags: ["ready"]);

if (engineOptions.Proxy.Enabled)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

if (engineOptions.Cors.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(engineOptions.Cors.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(engineOptions.RequestTimeoutSeconds),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    };
});

if (engineOptions.RateLimit.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (rejected, cancellationToken) =>
        {
            rejected.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await WriteProblemAsync(
                rejected.HttpContext,
                StatusCodes.Status429TooManyRequests,
                ApiErrors.RateLimited,
                "Se superó el límite de peticiones.");
        };
        options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = engineOptions.RateLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(engineOptions.RateLimit.WindowSeconds),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    });
}

var app = builder.Build();

if (engineOptions.Proxy.Enabled) app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    await WriteProblemAsync(
        httpContext,
        httpContext.Response.StatusCode,
        ApiErrors.CodeForStatus(httpContext.Response.StatusCode),
        "La petición no pudo atenderse.");
});

if (engineOptions.Cors.AllowedOrigins.Length > 0) app.UseCors();

app.Use(async (context, next) =>
{
    // Se lee en cada petición para permitir configuración por entorno y pruebas de integración.
    var apiKey = context.RequestServices
        .GetRequiredService<IOptions<StatisticEngineOptions>>()
        .Value.Auth.ApiKey;
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        await next();
        return;
    }

    var isInfrastructure = context.Request.Path.StartsWithSegments("/health")
                           || context.Request.Path.StartsWithSegments("/openapi")
                           || context.Request.Path.StartsWithSegments("/scalar");
    // El preflight CORS nunca lleva credenciales: debe pasar para que el navegador lo resuelva.
    if (isInfrastructure || HttpMethods.IsOptions(context.Request.Method))
    {
        await next();
        return;
    }

    var provided = context.Request.Headers.TryGetValue("X-Api-Key", out var values) ? values.ToString() : string.Empty;
    if (!string.Equals(provided, apiKey, StringComparison.Ordinal))
    {
        await ApiErrors.Create(
            StatusCodes.Status401Unauthorized,
            ApiErrors.Unauthorized,
            "Clave de API ausente o inválida.",
            context).ExecuteAsync(context);
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-StatisticEngine-Version"] = EngineInfo.Version;
    await next();
});

if (engineOptions.RateLimit.Enabled) app.UseRateLimiter();
app.UseRequestTimeouts();

app.MapOpenApi();
if (app.Environment.IsDevelopment()) app.MapScalarApiReference();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthAsync
});

var api = app.MapGroup("/api/v1");
api.MapAnalysesEndpoints();
api.MapDistributionEndpoints();
api.MapMetaEndpoints();
if (engineOptions.RateLimit.Enabled) api.RequireRateLimiting("api");

app.Run();

static async Task WriteProblemAsync(HttpContext context, int status, string code, string title)
{
    var problem = new ProblemDetails { Status = status, Title = title };
    problem.Extensions["code"] = code;
    problem.Extensions["traceId"] = context.TraceIdentifier;
    await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = problem
    });
}

static Task WriteHealthAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        checks = report.Entries
            .Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description
            })
            .ToArray()
    });
}

/// <summary>Punto de entrada expuesto para las pruebas de integración (<c>WebApplicationFactory</c>).</summary>
public partial class Program;
