<#
.SYNOPSIS
    Installa ed avvia il container Docker di EMMA-SERVER.

.DESCRIPTION
    Scarica l'immagine almalabs/emma-server:latest e avvia il container leggendo
    la configurazione da un file di testo (default: emma-server.env accanto allo
    script). Ogni riga del file e' una variabile d'ambiente nel formato

        Chiave__SottoChiave=valore

    dove il doppio underscore "__" sostituisce i due punti delle sezioni di
    appsettings.json (es. Database:Host -> Database__Host). Le variabili
    d'ambiente hanno la precedenza su appsettings.json, quindi non serve
    ricostruire l'immagine per cambiare la configurazione.

.PARAMETER ConfigFile
    Percorso del file di configurazione. Default: emma-server.env nella stessa
    cartella dello script.

.PARAMETER GenerateConfig
    Crea il file di configurazione di esempio (con tutte le chiavi disponibili)
    e termina. Non sovrascrive un file gia' esistente se non si usa -Force.

.PARAMETER Image
    Immagine da usare. Default: almalabs/emma-server:latest

.PARAMETER ContainerName
    Nome del container. Default: emma-server

.PARAMETER Port
    Porta sull'host da mappare sulla 8080 del container. Default: 9111
    (la porta interna resta sempre la 8080, definita da ASPNETCORE_URLS).

.PARAMETER RestartPolicy
    Politica di riavvio Docker. Default: unless-stopped

.PARAMETER Network
    Rete Docker a cui agganciare il container. Viene creata se non esiste.
    Su una rete definita dall'utente Docker fa da DNS, quindi gli altri
    container (es. emma-web) raggiungono questo con http://<nome>:8080
    usando la porta INTERNA, non quella pubblicata con -Port.
    Default: emma-net. Passare stringa vuota per non usare nessuna rete.

.PARAMETER Volume
    Bind mount aggiuntivi, formato "percorso-host:percorso-container".
    Ripetibile: -Volume "C:\emma\dati:/app/dati","C:\emma\log:/app/log"

.PARAMETER NoPull
    Non esegue il pull dell'immagine (usa quella gia' presente in locale).

.PARAMETER Force
    Rimuove senza chiedere conferma un container esistente con lo stesso nome
    (e, con -GenerateConfig, sovrascrive il file di configurazione).

.PARAMETER DryRun
    Mostra i comandi che verrebbero eseguiti senza eseguirli.

.PARAMETER Follow
    Al termine resta agganciato ai log del container (Ctrl+C per staccarsi:
    il container continua a girare).

.EXAMPLE
    .\install-emma-server.ps1 -GenerateConfig
    Crea emma-server.env da compilare.

.EXAMPLE
    .\install-emma-server.ps1
    Avvia il container leggendo emma-server.env: http://localhost:9111

.EXAMPLE
    .\install-emma-server.ps1 -ConfigFile .\produzione.env -Port 9090 -Force
    Avvia sulla porta 9090 con un file di configurazione diverso, ricreando il
    container se gia' esistente.
#>

[CmdletBinding()]
param(
    [string]   $ConfigFile,
    [switch]   $GenerateConfig,
    [string]   $Image         = 'almalabs/emma-server:latest',
    [string]   $ContainerName = 'emma-server',
    [int]      $Port          = 9111,
    [ValidateSet('no', 'always', 'on-failure', 'unless-stopped')]
    [string]   $RestartPolicy = 'unless-stopped',
    [string]   $Network       = 'emma-net',
    [string[]] $Volume        = @(),
    [switch]   $NoPull,
    [switch]   $Force,
    [switch]   $DryRun,
    [switch]   $Follow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Utilita' di output
# ---------------------------------------------------------------------------

function Write-Step { param([string]$Messaggio) Write-Host "==> $Messaggio" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Messaggio) Write-Host "    $Messaggio" -ForegroundColor Green }
function Write-Warn { param([string]$Messaggio) Write-Host "    $Messaggio" -ForegroundColor Yellow }

function Stop-ConErrore {
    param([string]$Messaggio)
    Write-Host ""
    Write-Host "ERRORE: $Messaggio" -ForegroundColor Red
    exit 1
}

# Esegue docker catturando stdout+stderr; restituisce l'output e imposta $script:UltimoExitCode.
# ErrorActionPreference viene abbassato perche' con 2>&1 alcune versioni di
# PowerShell trasformano lo stderr dei comandi nativi in un errore terminante.
function Invoke-Docker {
    param([string[]]$Argomenti)
    $precedente = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & docker @Argomenti 2>&1
        $script:UltimoExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $precedente
    }
    return ($output | Out-String).Trim()
}

# ---------------------------------------------------------------------------
# Percorsi di default
# ---------------------------------------------------------------------------

$CartellaScript = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($CartellaScript)) { $CartellaScript = (Get-Location).Path }

if ([string]::IsNullOrWhiteSpace($ConfigFile)) {
    $ConfigFile = Join-Path $CartellaScript 'emma-server.env'
}

# ---------------------------------------------------------------------------
# Template del file di configurazione
# ---------------------------------------------------------------------------

$TemplateConfig = @'
# =============================================================================
# EMMA-SERVER - configurazione del container
# =============================================================================
# Ogni riga e' una variabile d'ambiente passata al container.
# Il doppio underscore "__" corrisponde ai due punti delle sezioni di
# appsettings.json:  Database:Master:Password  ->  Database__Master__Password
# I valori impostati qui hanno la precedenza su appsettings.json.
#
# Righe vuote e righe che iniziano con # vengono ignorate.
# Il valore va scritto cosi' com'e' (niente virgolette obbligatorie, niente
# escape): tutto quello che sta dopo il primo "=" fa parte del valore.
# Le chiavi lasciate vuote NON vengono passate al container: vale il valore
# presente in appsettings.json dentro l'immagine.
# =============================================================================

# --- Runtime ASP.NET ---------------------------------------------------------
# ASPNETCORE_URLS e' la porta INTERNA al container: lasciarla sulla 8080.
# La porta esposta sull'host si cambia con -Port (default 9111).
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080

# --- Connessione usata da Dapper (ConnectionStrings:DefaultConnection) -------
# Lasciare vuoto se la connessione viene costruita dalla sezione Database.
ConnectionStrings__DefaultConnection=

# --- Database ----------------------------------------------------------------
Database__Host=
Database__UserName=
Database__Password=
Database__Database=
Database__Master__Name=postgres
Database__Master__User=postgres
Database__Master__Password=
Database__Emma__Name=emma
Database__Emma__User=postgres
Database__Emma__Password=

# --- Servizio EMMA-AI --------------------------------------------------------
# Attenzione: il nome della sezione contiene un trattino, va scritto cosi'.
EMMA-AI__Endpoint=https://emma-aegc.onrender.com
EMMA-AI__Model=GEMINI
EMMA-AI__ApiKey=

# --- Password amministratore -------------------------------------------------
Admin__Password=

# --- Google Gemini (embedding) ----------------------------------------------
Gemini__ApiKey=
Gemini__Model=gemini-embedding-001
Gemini__UseEmbedContentConfig=false
Gemini__RequestsPerMinute=100
Gemini__MaxRateLimitRetries=3
Gemini__MaxRetryWaitSeconds=120

# --- Pipeline RAG ------------------------------------------------------------
Rag__ChunkSize=512
Rag__ChunkOverlap=64
Rag__EmbeddingDim=768
Rag__BatchSize=25
Rag__TopK=5
Rag__HybridSearch=true

# --- Import bolle da casella IMAP -------------------------------------------
ImportBatch__Enabled=false
ImportBatch__Server=https://emma-server-uda8.onrender.com
ImportBatch__ImapServer=imap.gmail.com
ImportBatch__ImapUser=
ImportBatch__ImapPassword=
ImportBatch__Minutes=10

# --- Import documenti da casella IMAP ---------------------------------------
ImportBatchDoc__Enabled=false
ImportBatchDoc__Server=https://emma-server-uda8.onrender.com
ImportBatchDoc__ImapServer=imap.gmail.com
ImportBatchDoc__ImapUser=
ImportBatchDoc__ImapPassword=
ImportBatchDoc__Minutes=10

# --- Pulizia dati ------------------------------------------------------------
CleanData__Enabled=false

# --- Log ---------------------------------------------------------------------
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
'@

# ---------------------------------------------------------------------------
# -GenerateConfig
# ---------------------------------------------------------------------------

if ($GenerateConfig) {
    if ((Test-Path -LiteralPath $ConfigFile) -and -not $Force) {
        Stop-ConErrore "Il file '$ConfigFile' esiste gia'. Usare -Force per sovrascriverlo."
    }
    $codifica = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($ConfigFile, $TemplateConfig, $codifica)
    Write-Ok "Creato il file di configurazione: $ConfigFile"
    Write-Host ""
    Write-Host "Compilare le chiavi necessarie (almeno Database__*, Admin__Password," -ForegroundColor Yellow
    Write-Host "Gemini__ApiKey) e poi rilanciare lo script senza -GenerateConfig." -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------------------
# Controlli preliminari su Docker
# ---------------------------------------------------------------------------

Write-Step "Controllo di Docker"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Stop-ConErrore "Docker non e' installato (o non e' nel PATH). Installare Docker Desktop e riprovare."
}

$versione = Invoke-Docker @('version', '--format', '{{.Server.Version}}')
if ($script:UltimoExitCode -ne 0) {
    Stop-ConErrore "Il daemon Docker non risponde. Avviare Docker Desktop e riprovare.`n$versione"
}
Write-Ok "Docker Engine $versione"

# ---------------------------------------------------------------------------
# Lettura del file di configurazione
# ---------------------------------------------------------------------------

Write-Step "Lettura della configurazione"

if (-not (Test-Path -LiteralPath $ConfigFile)) {
    Stop-ConErrore "File di configurazione non trovato: $ConfigFile`nCrearlo con:  .\$($MyInvocation.MyCommand.Name) -GenerateConfig"
}

$variabili = [ordered]@{}
$numeroRiga = 0
$ignorate   = 0

foreach ($riga in (Get-Content -LiteralPath $ConfigFile -Encoding UTF8)) {
    $numeroRiga++
    $testo = $riga.Trim()

    if ($testo.Length -eq 0)      { continue }
    if ($testo.StartsWith('#'))   { continue }
    if ($testo.StartsWith(';'))   { continue }

    $posizione = $testo.IndexOf('=')
    if ($posizione -lt 1) {
        Write-Warn "Riga $numeroRiga ignorata (manca '='): $testo"
        continue
    }

    $chiave  = $testo.Substring(0, $posizione).Trim()
    $valore  = $testo.Substring($posizione + 1).Trim()

    # Toglie un'eventuale coppia di virgolette che racchiude tutto il valore
    if ($valore.Length -ge 2 -and
        (($valore.StartsWith('"') -and $valore.EndsWith('"')) -or
         ($valore.StartsWith("'") -and $valore.EndsWith("'")))) {
        $valore = $valore.Substring(1, $valore.Length - 2)
    }

    if ([string]::IsNullOrEmpty($valore)) { $ignorate++; continue }

    if ($variabili.Contains($chiave)) {
        Write-Warn "Chiave duplicata '$chiave': vale l'ultimo valore (riga $numeroRiga)."
    }
    $variabili[$chiave] = $valore
}

if ($variabili.Count -eq 0) {
    Stop-ConErrore "Nessuna variabile valorizzata in '$ConfigFile'."
}

Write-Ok "$($variabili.Count) variabili lette da $(Split-Path -Leaf $ConfigFile) ($ignorate chiavi vuote saltate)"

# Avvisi sulle chiavi critiche
foreach ($obbligatoria in @('Database__Host', 'Database__UserName', 'Database__Password', 'Database__Database')) {
    if (-not $variabili.Contains($obbligatoria)) {
        Write-Warn "$obbligatoria non valorizzata: verra' usato il valore di appsettings.json."
    }
}

# ---------------------------------------------------------------------------
# Pull dell'immagine
# ---------------------------------------------------------------------------

if ($NoPull) {
    Write-Step "Pull saltato (-NoPull)"
} else {
    Write-Step "Download dell'immagine $Image"
    if ($DryRun) {
        Write-Host "    docker pull $Image" -ForegroundColor DarkGray
    } else {
        & docker pull $Image
        if ($LASTEXITCODE -ne 0) {
            Stop-ConErrore "Pull di '$Image' fallito. Se il repository e' privato eseguire prima 'docker login'."
        }
        Write-Ok "Immagine aggiornata"
    }
}

# ---------------------------------------------------------------------------
# Container esistente
# ---------------------------------------------------------------------------

Write-Step "Controllo del container '$ContainerName'"

$esistente = Invoke-Docker @('ps', '-a', '--filter', "name=^/$ContainerName$", '--format', '{{.ID}}')

if (-not [string]::IsNullOrWhiteSpace($esistente)) {
    if (-not $Force -and -not $DryRun) {
        Write-Host ""
        Write-Host "    Esiste gia' un container chiamato '$ContainerName'." -ForegroundColor Yellow
        $risposta = Read-Host "    Lo rimuovo e lo ricreo? [s/N]"
        if ($risposta -notmatch '^[sSyY]') {
            Stop-ConErrore "Operazione annullata."
        }
    }
    if ($DryRun) {
        Write-Host "    docker rm -f $ContainerName" -ForegroundColor DarkGray
    } else {
        $null = Invoke-Docker @('rm', '-f', $ContainerName)
        if ($script:UltimoExitCode -ne 0) {
            Stop-ConErrore "Impossibile rimuovere il container esistente."
        }
        Write-Ok "Container precedente rimosso"
    }
} else {
    Write-Ok "Nessun container precedente"
}

# ---------------------------------------------------------------------------
# Rete Docker
# ---------------------------------------------------------------------------
# Sulla bridge di default Docker non risolve i nomi dei container: serve una
# rete definita dall'utente perche' emma-web possa chiamare http://emma-server:8080

if (-not [string]::IsNullOrWhiteSpace($Network)) {
    Write-Step "Rete Docker '$Network'"

    $reteEsistente = Invoke-Docker @('network', 'ls', '--filter', "name=^$Network$", '--format', '{{.Name}}')

    if ([string]::IsNullOrWhiteSpace($reteEsistente)) {
        if ($DryRun) {
            Write-Host "    docker network create $Network" -ForegroundColor DarkGray
        } else {
            $null = Invoke-Docker @('network', 'create', $Network)
            if ($script:UltimoExitCode -ne 0) {
                Stop-ConErrore "Creazione della rete '$Network' fallita."
            }
            Write-Ok "Rete creata"
        }
    } else {
        Write-Ok "Rete gia' presente"
    }
} else {
    Write-Step "Nessuna rete specificata (bridge di default)"
}

# ---------------------------------------------------------------------------
# File env temporaneo
# ---------------------------------------------------------------------------
# Le variabili vengono passate con --env-file invece che con tanti -e: cosi'
# password e API key non finiscono nella riga di comando (visibile agli altri
# processi) ne' nella cronologia di PowerShell.

$fileEnvTemporaneo = Join-Path ([System.IO.Path]::GetTempPath()) ("emma-server-{0}.env" -f ([guid]::NewGuid().ToString('N')))

$righeEnv = foreach ($chiave in $variabili.Keys) { "$chiave=$($variabili[$chiave])" }
$codifica = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($fileEnvTemporaneo, [string[]]$righeEnv, $codifica)

try {
    # -----------------------------------------------------------------------
    # Avvio del container
    # -----------------------------------------------------------------------

    Write-Step "Avvio del container"

    $argomenti = @(
        'run', '-d',
        '--name', $ContainerName,
        '--restart', $RestartPolicy,
        '-p', "$($Port):8080",
        '--env-file', $fileEnvTemporaneo
    )

    if (-not [string]::IsNullOrWhiteSpace($Network)) {
        $argomenti += @('--network', $Network)
    }

    foreach ($montaggio in $Volume) {
        if (-not [string]::IsNullOrWhiteSpace($montaggio)) {
            $argomenti += @('-v', $montaggio)
        }
    }

    $argomenti += $Image

    if ($DryRun) {
        Write-Host ""
        Write-Host "    docker run -d \" -ForegroundColor DarkGray
        Write-Host "      --name $ContainerName \" -ForegroundColor DarkGray
        Write-Host "      --restart $RestartPolicy \" -ForegroundColor DarkGray
        Write-Host "      -p $($Port):8080 \" -ForegroundColor DarkGray
        if (-not [string]::IsNullOrWhiteSpace($Network)) {
            Write-Host "      --network $Network \" -ForegroundColor DarkGray
        }
        foreach ($chiave in $variabili.Keys) {
            $mostrato = if ($chiave -match 'Password|ApiKey|Key$|Secret') { '***' } else { $variabili[$chiave] }
            Write-Host "      -e `"$chiave=$mostrato`" \" -ForegroundColor DarkGray
        }
        foreach ($montaggio in $Volume) { Write-Host "      -v `"$montaggio`" \" -ForegroundColor DarkGray }
        Write-Host "      $Image" -ForegroundColor DarkGray
        Write-Host ""
        Write-Ok "Dry run: nessuna modifica applicata."
        exit 0
    }

    $idContainer = Invoke-Docker $argomenti
    if ($script:UltimoExitCode -ne 0) {
        Stop-ConErrore "Avvio del container fallito.`n$idContainer"
    }
    Write-Ok "Container avviato ($($idContainer.Substring(0, [Math]::Min(12, $idContainer.Length))))"

    # -----------------------------------------------------------------------
    # Verifica che resti su
    # -----------------------------------------------------------------------

    Write-Step "Verifica dello stato"
    Start-Sleep -Seconds 4

    $stato = Invoke-Docker @('inspect', '-f', '{{.State.Status}}', $ContainerName)

    if ($stato -ne 'running') {
        Write-Host ""
        Write-Host "Il container non e' in esecuzione (stato: $stato). Ultimi log:" -ForegroundColor Red
        Write-Host ""
        & docker logs --tail 40 $ContainerName
        Stop-ConErrore "Controllare la configurazione in '$ConfigFile'."
    }

    Write-Ok "Stato: running"
    Write-Host ""
    Write-Host "  Dall'host       : http://localhost:$Port" -ForegroundColor Green
    if (-not [string]::IsNullOrWhiteSpace($Network)) {
        Write-Host "  Da altri container sulla rete '$Network' : http://$($ContainerName):8080" -ForegroundColor Green
        Write-Host "  (dentro Docker vale la porta interna 8080, non la $Port)" -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "  Log        : docker logs -f $ContainerName"
    Write-Host "  Stop       : docker stop $ContainerName"
    Write-Host "  Riavvio    : docker restart $ContainerName"
    Write-Host "  Rimozione  : docker rm -f $ContainerName"
    Write-Host ""

    if ($Follow) {
        Write-Host "  (Ctrl+C stacca i log, il container continua a girare)" -ForegroundColor DarkGray
        Write-Host ""
        & docker logs -f $ContainerName
    }
}
finally {
    # Il file con le credenziali non deve restare sul disco
    if (Test-Path -LiteralPath $fileEnvTemporaneo) {
        Remove-Item -LiteralPath $fileEnvTemporaneo -Force -ErrorAction SilentlyContinue
    }
}
