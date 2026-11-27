#Requires -Version 7.0
<#
.SYNOPSIS
    Publica e instala StatisticEngine como servicio de Windows para colocarlo detrás de Apache.

.DESCRIPTION
    El servicio escucha SOLO en loopback (127.0.0.1:<Port>); Apache es la única entrada pública.
    Usa NSSM como envoltorio de servicio (https://nssm.cc), así no hace falta cambiar el código
    para añadir Microsoft.Extensions.Hosting.WindowsServices.

.PARAMETER Action
    publish   Compila y publica en -TargetDirectory (no requiere administrador).
    install   Publica, crea el servicio, configura variables de entorno y lo arranca (administrador).
    update    Publica y reinicia el servicio existente (administrador).
    uninstall Detiene y elimina el servicio (administrador). No borra los archivos publicados.
    status    Muestra el estado del servicio y comprueba /health/live.

.EXAMPLE
    .\install-service.ps1 -Action publish -TargetDirectory D:\temp\statisticengine

.EXAMPLE
    .\install-service.ps1 -Action install -ApiKey "una-clave-larga-y-aleatoria"

.EXAMPLE
    .\install-service.ps1 -Action status
#>
[CmdletBinding()]
param(
    [ValidateSet('publish', 'install', 'update', 'uninstall', 'status')]
    [string]$Action = 'status',

    [string]$ServiceName = 'StatisticEngine',

    [string]$TargetDirectory = 'C:\Services\StatisticEngine',

    [int]$Port = 5080,

    [string]$ApiKey = '',

    [string]$NssmPath = 'nssm.exe'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repoRoot 'src\StatisticEngine.Api\StatisticEngine.Api.csproj'
$executable = Join-Path $TargetDirectory 'StatisticEngine.Api.exe'
$logDirectory = Join-Path $TargetDirectory 'logs'
$baseUrl = "http://127.0.0.1:$Port"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "La acción '$Action' requiere una consola de PowerShell como administrador."
    }
}

function Get-Nssm {
    $command = Get-Command $NssmPath -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "No se encontró NSSM ('$NssmPath'). Descárgalo de https://nssm.cc/download, descomprímelo, añade la carpeta al PATH o pasa -NssmPath 'C:\ruta\nssm.exe'."
    }
    return $command.Source
}

function Invoke-Publish {
    Write-Host "Publicando en $TargetDirectory ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $TargetDirectory | Out-Null
    & dotnet publish $project -c Release -o $TargetDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló con código $LASTEXITCODE." }
    if (-not (Test-Path $executable)) { throw "No se generó $executable." }
    Write-Host "Publicado: $executable" -ForegroundColor Green
}

function Wait-Health {
    param([int]$TimeoutSeconds = 60)

    Write-Host "Comprobando $baseUrl/health/live ..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-RestMethod "$baseUrl/health/live" -TimeoutSec 5
            if ($response.status -eq 'Healthy') {
                Write-Host "Servicio saludable en $baseUrl" -ForegroundColor Green
                return
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    throw "El servicio no respondió en $baseUrl/health/live en $TimeoutSeconds s. Revisa $logDirectory."
}

switch ($Action) {
    'publish' {
        Invoke-Publish
        Write-Host "Siguiente paso: .\install-service.ps1 -Action install (como administrador)" -ForegroundColor Cyan
    }

    'install' {
        Assert-Administrator
        $nssm = Get-Nssm
        Invoke-Publish
        New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

        if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
            throw "El servicio '$ServiceName' ya existe. Usa -Action update o -Action uninstall."
        }

        & $nssm install $ServiceName $executable
        & $nssm set $ServiceName AppDirectory $TargetDirectory
        & $nssm set $ServiceName Start SERVICE_AUTO_START
        & $nssm set $ServiceName AppExit Default Restart
        & $nssm set $ServiceName AppStdout (Join-Path $logDirectory 'service.log')
        & $nssm set $ServiceName AppStderr (Join-Path $logDirectory 'service.error.log')
        & $nssm set $ServiceName AppRotateFiles 1

        $environment = @(
            'ASPNETCORE_ENVIRONMENT=Production',
            "ASPNETCORE_URLS=$baseUrl",
            'StatisticEngine__Proxy__Enabled=true'
        )
        if ($ApiKey) { $environment += "StatisticEngine__Auth__ApiKey=$ApiKey" }
        & $nssm set $ServiceName AppEnvironmentExtra @environment

        & $nssm start $ServiceName
        if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar el servicio '$ServiceName'." }

        Wait-Health
        Write-Host ''
        Write-Host "Servicio instalado. Ahora configura Apache:" -ForegroundColor Cyan
        Write-Host "  1. Copia deploy\apache\statisticengine.conf a la carpeta conf\extra de Apache."
        Write-Host "  2. Añade 'Include conf/extra/statisticengine.conf' a httpd.conf."
        Write-Host "  3. httpd.exe -t  &&  Restart-Service Apache2.4"
        Write-Host "  4. Verifica: curl.exe -H `"X-Api-Key: <clave>`" https://<tu-dominio>/health/ready"
    }

    'update' {
        Assert-Administrator
        if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
            throw "El servicio '$ServiceName' no existe. Usa -Action install."
        }
        Invoke-Publish
        Restart-Service -Name $ServiceName -Force
        Wait-Health
    }

    'uninstall' {
        Assert-Administrator
        $nssm = Get-Nssm
        if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
            Write-Host "El servicio '$ServiceName' no existe." -ForegroundColor Yellow
            return
        }
        & $nssm stop $ServiceName
        & $nssm remove $ServiceName confirm
        Write-Host "Servicio eliminado. Los archivos de $TargetDirectory NO se han borrado." -ForegroundColor Green
    }

    'status' {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service) {
            Write-Host "Servicio '$ServiceName': $($service.Status)" -ForegroundColor Cyan
        }
        else {
            Write-Host "El servicio '$ServiceName' no está instalado." -ForegroundColor Yellow
        }
        try {
            $health = Invoke-RestMethod "$baseUrl/health/ready" -TimeoutSec 5
            Write-Host "/health/ready: $($health.status) en $baseUrl" -ForegroundColor Green
        }
        catch {
            Write-Host "Sin respuesta en $baseUrl/health/ready" -ForegroundColor Yellow
        }
    }
}
