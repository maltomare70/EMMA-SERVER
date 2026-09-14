<#
.SYNOPSIS
    Build, tag e push dell'immagine Docker almalabs/emma-server.

.DESCRIPTION
    Esegue il login su Docker Hub (token via variabile d'ambiente DOCKER_PAT
    o richiesto a runtime), quindi build e push dell'immagine.

.EXAMPLE
    # Imposta il token una volta per utente (consigliato):
    [Environment]::SetEnvironmentVariable("DOCKER_PAT", "il-tuo-token", "User")

    # Poi:
    .\publish-emma-server.ps1

.EXAMPLE
    .\publish-emma-server.ps1 -Tag "1.4.0" -Latest
#>

[CmdletBinding()]
param(
    [string]$ProjectRoot = "C:\EMMA-DOC-PLATFORM\EMMA-SERVER",
    [string]$Dockerfile  = "EmmaServer/Dockerfile",
    [string]$Image       = "almalabs/emma-server",
    [string]$Tag         = "latest",
    [string]$Username    = "almalabs",

    # Se usato con -Tag <versione>, pusha anche il tag :latest
    [switch]$Latest,

    # Salta il docker login (se sei gia' autenticato)
    [switch]$SkipLogin
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description -- fallito (exit code $LASTEXITCODE)"
    }
}

try {
    # --- Controlli preliminari -------------------------------------------
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker non trovato nel PATH. Avvia Docker Desktop e riprova."
    }

    if (-not (Test-Path -LiteralPath $ProjectRoot)) {
        throw "Cartella di progetto non trovata: $ProjectRoot"
    }

    Push-Location -LiteralPath $ProjectRoot
    Write-Host "Directory di lavoro: $ProjectRoot" -ForegroundColor DarkGray

    if (-not (Test-Path -LiteralPath $Dockerfile)) {
        throw "Dockerfile non trovato: $Dockerfile (relativo a $ProjectRoot)"
    }

    # --- Login ------------------------------------------------------------
    if (-not $SkipLogin) {
        $pat = $env:DOCKER_PAT
        if ([string]::IsNullOrWhiteSpace($pat)) {
            $secure = Read-Host -Prompt "Docker Hub PAT per '$Username'" -AsSecureString
            $pat = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            )
        }
        if ([string]::IsNullOrWhiteSpace($pat)) { throw "Token vuoto: login annullato." }

        Write-Host "==> Login su Docker Hub come '$Username'" -ForegroundColor Cyan
        $pat | docker login -u $Username --password-stdin
        if ($LASTEXITCODE -ne 0) { throw "docker login fallito." }
        $pat = $null
    }

    # --- Build ------------------------------------------------------------
    $target = "${Image}:${Tag}"
    Invoke-Step "Build di $target" { docker build -f $Dockerfile -t $target . }

    # --- Push -------------------------------------------------------------
    Invoke-Step "Push di $target" { docker push $target }

    if ($Latest -and $Tag -ne "latest") {
        $latestRef = "${Image}:latest"
        Invoke-Step "Tag $target -> $latestRef" { docker tag $target $latestRef }
        Invoke-Step "Push di $latestRef"        { docker push $latestRef }
    }

    Write-Host ""
    Write-Host "Completato: $target pubblicato su Docker Hub." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "ERRORE: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ((Get-Location).Path -ne $PSScriptRoot) { Pop-Location -ErrorAction SilentlyContinue }
}