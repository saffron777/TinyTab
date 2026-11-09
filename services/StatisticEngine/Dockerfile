# Imagen del microservicio StatisticEngine (.NET 10).
# Compilación en dos etapas: SDK para restaurar y publicar, runtime ASP.NET Core no root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Se copian primero los archivos de configuración y los csproj para aprovechar la caché de capas.
COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/StatisticEngine.Domain/StatisticEngine.Domain.csproj src/StatisticEngine.Domain/
COPY src/StatisticEngine.Application/StatisticEngine.Application.csproj src/StatisticEngine.Application/
COPY src/StatisticEngine.Api/StatisticEngine.Api.csproj src/StatisticEngine.Api/
RUN dotnet restore src/StatisticEngine.Api/StatisticEngine.Api.csproj

COPY src/ src/
RUN dotnet publish src/StatisticEngine.Api/StatisticEngine.Api.csproj \
        -c Release \
        -o /app \
        --no-restore \
        -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_NOLOGO=1

COPY --from=build /app ./

# La imagen oficial de ASP.NET Core define APP_UID (1654) para ejecutar sin privilegios.
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "StatisticEngine.Api.dll"]
