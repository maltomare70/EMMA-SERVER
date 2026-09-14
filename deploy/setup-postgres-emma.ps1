<#
.SYNOPSIS
    Scarica, configura e avvia un container Docker PostgreSQL in locale.

.DESCRIPTION
    - Verifica che Docker sia installato e in esecuzione
    - Crea (se serve) la rete Docker condivisa con EMMA-SERVER
    - Crea la cartella dati su disco (default C:\data)
    - Scarica l'immagine postgres (pgvector/pgvector: Postgres + estensione vector)
    - Crea il container con utente "emma", password "nocafla" e database "emma-db"
    - Abilita l'estensione "vector" (pgvector) sul database
    - Attende che il database sia pronto e stampa i dati di connessione

.PARAMETER DataPath
    Cartella Windows in cui salvare i file del database. Default: C:\data

.PARAMETER NetworkName
    Rete Docker a cui collegare il container, la stessa usata da EMMA-SERVER.
    Viene creata se non esiste. Default: emma-net

.PARAMETER Image
    Immagine Docker da usare. Default: pgvector/pgvector:pg<PostgresVersion>,
    cioe' l'immagine ufficiale Postgres con pgvector gia' compilato dentro.
    L'immagine "postgres:<versione>" NON contiene pgvector e fa fallire
    l'applicazione con: extension "vector" is not available.

.PARAMETER SkipVectorExtension
    Non esegue CREATE EXTENSION vector (utile se l'immagine scelta non ha pgvector).

.PARAMETER UseVolume
    Usa un volume Docker gestito invece del bind mount su disco.
    Serve come fallback: su Windows il bind mount di una cartella NTFS
    a volte non e' compatibile con i permessi richiesti da initdb.

.EXAMPLE
    .\setup-postgres-emma.ps1
    .\setup-postgres-emma.ps1 -Port 5433 -PostgresVersion 16
    .\setup-postgres-emma.ps1 -UseVolume -Force
    .\setup-postgres-emma.ps1 -Image 'postgres:17' -SkipVectorExtension
#>

[CmdletBinding()]
param(
    [string] $DataPath        = 'C:\data',
    [string] $ContainerName   = 'postgres-emma',
    [string] $NetworkName     = 'emma-net',
    [string] $PostgresVersion = '17',
    [int]    $Port            = 5432,
    [string] $DbUser          = 'emma',
    [string] $DbPassword      = 'nocafla',
    [string] $DbName          = 'emma-db',
    [string] $Image           = '',     # default: pgvector/pgvector:pg<PostgresVersion>
    [switch] $SkipVectorExtension,
    [switch] $UseVolume,
    [switch] $Force            # rimuove senza chiedere un container omonimo gia' esistente
)

$ErrorActionPreference = 'Stop'
$VolumeName = 'emma-pgdata'

<#
    Immagine: si usa pgvector/pgvector, che e' l'immagine ufficiale di Postgres
    (stesso entrypoint, stesse variabili POSTGRES_*) con in piu' i binari di
    pgvector installati. Senza di essi "CREATE EXTENSION vector" fallisce con
    l'errore 0A000: extension "vector" is not available, perche' l'estensione
    non e' semplicemente disabilitata: manca proprio dal server.
    I tag seguono lo schema pg13 ... pg17.
#>
if (-not $Image) { $Image = "pgvector/pgvector:pg$PostgresVersion" }

function Write-Step { param($m) Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param($m) Write-Host "    $m" -ForegroundColor Yellow }

<#
    Invoke-Docker: esegue docker separando stdout da stderr.

    PowerShell 5.1, con $ErrorActionPreference = 'Stop', converte ogni riga
    che un comando nativo scrive su stderr in un ErrorRecord terminante
    (NativeCommandError) - anche quando si tratta di un semplice WARNING,
    per esempio "WARNING: No blkio throttle.read_bps_device support".
    Qui l'ErrorActionPreference viene abbassato per la durata della chiamata
    e le due stream vengono divise a mano:
      - l'output "vero" e' il valore di ritorno
      - lo stderr finisce in $script:DockerStderr
      - il codice di uscita in $script:DockerExit
#>
function Invoke-Docker {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $DockerArgs
    )

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = & docker @DockerArgs 2>&1
        $script:DockerExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prev
    }

    $stdout = @()
    $stderr = @()
    foreach ($line in @($raw)) {
        if ($line -is [System.Management.Automation.ErrorRecord]) { $stderr += [string]$line }
        else                                                     { $stdout += [string]$line }
    }
    $script:DockerStderr = ($stderr -join [Environment]::NewLine)

    return $stdout
}

# ---------------------------------------------------------------- 1. Docker
Write-Step 'Verifica di Docker'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker non e'' installato (o non e'' nel PATH). Installa Docker Desktop: https://www.docker.com/products/docker-desktop/'
}

Invoke-Docker @('info', '--format', '{{.ServerVersion}}') | Out-Null
if ($script:DockerExit -ne 0) {
    Write-Warn $script:DockerStderr
    throw 'Docker e'' installato ma il demone non risponde. Avvia Docker Desktop e riprova.'
}
Write-Ok 'Docker operativo.'

# ---------------------------------------------------------------- 2. Porta
Write-Step "Verifica della porta $Port"

$inUse = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($inUse) {
    throw "La porta $Port e'' gia'' occupata. Rilancia lo script con -Port <altra porta>, es. -Port 5433."
}
Write-Ok "Porta $Port libera."

# ---------------------------------------------------------------- 3. Rete
Write-Step "Verifica della rete Docker '$NetworkName'"

$net = Invoke-Docker @('network', 'ls', '--filter', "name=^$NetworkName$", '--format', '{{.Name}}')
if (-not $net) {
    Invoke-Docker @('network', 'create', $NetworkName) | Out-Null
    if ($script:DockerExit -ne 0) {
        Write-Warn $script:DockerStderr
        throw "Creazione della rete '$NetworkName' fallita."
    }
    Write-Ok "Rete '$NetworkName' creata."
}
else {
    Write-Ok "Rete '$NetworkName' gia'' presente."
}

# mostra chi e' gia' collegato (utile per confermare che EMMA-SERVER c'e')
$peers = (Invoke-Docker @('network', 'inspect', $NetworkName, '--format', '{{range .Containers}}{{.Name}} {{end}}')) -join ' '
if ($peers -and $peers.Trim()) {
    Write-Ok "Container gia'' collegati: $($peers.Trim())"
    if ($peers -notmatch 'EMMA-SERVER') {
        Write-Warn "Attenzione: EMMA-SERVER non risulta collegato a '$NetworkName'."
        Write-Warn "Se e'' in esecuzione su un'altra rete:  docker network connect $NetworkName EMMA-SERVER"
    }
}
else {
    Write-Warn "Nessun container collegato a '$NetworkName' al momento."
    Write-Warn "Ricorda di collegare EMMA-SERVER:  docker network connect $NetworkName EMMA-SERVER"
}

# ---------------------------------------------------------------- 4. Storage
Write-Step 'Preparazione dello storage'

if ($UseVolume) {
    Invoke-Docker @('volume', 'create', $VolumeName) | Out-Null
    if ($script:DockerExit -ne 0) {
        Write-Warn $script:DockerStderr
        throw "Creazione del volume '$VolumeName' fallita."
    }
    $mountArg = "${VolumeName}:/var/lib/postgresql/data"
    Write-Ok "Volume Docker '$VolumeName' pronto."
}
else {
    if (-not (Test-Path -LiteralPath $DataPath)) {
        New-Item -ItemType Directory -Path $DataPath -Force -ErrorAction Stop | Out-Null
        Write-Ok "Cartella creata: $DataPath"
    }
    else {
        Write-Ok "Cartella gia'' presente: $DataPath"
    }
    $resolved = (Resolve-Path -LiteralPath $DataPath -ErrorAction Stop).Path
    $mountArg = "${resolved}:/var/lib/postgresql/data"
}

# ---------------------------------------------------------------- 5. Container esistente
Write-Step 'Controllo container esistenti'

$existing = Invoke-Docker @('ps', '-a', '--filter', "name=^/$ContainerName$", '--format', '{{.Names}}')
if ($existing) {
    if (-not $Force) {
        $answer = Read-Host "Esiste gia'' un container '$ContainerName'. Vuoi rimuoverlo e ricrearlo? (s/N)"
        if ($answer -notmatch '^[sSyY]$') {
            Write-Warn 'Operazione annullata. Nessuna modifica effettuata.'
            return
        }
    }
    Invoke-Docker @('rm', '-f', $ContainerName) | Out-Null
    Write-Ok "Container '$ContainerName' rimosso (i dati su disco restano intatti)."
}
else {
    Write-Ok 'Nessun container omonimo trovato.'
}

# ---------------------------------------------------------------- 6. Immagine
Write-Step "Download dell'immagine $Image"

# qui l'output va mostrato in diretta: stderr viene convertito in testo
$prev = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & docker pull $Image 2>&1 | ForEach-Object { Write-Host "    $_" }
    $pullExit = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $prev
}
if ($pullExit -ne 0) { throw "Download dell'immagine $Image fallito." }

# ---------------------------------------------------------------- 7. Avvio
Write-Step 'Creazione e avvio del container'

$runArgs = @(
    'run', '-d',
    '--name', $ContainerName,
    '--restart', 'unless-stopped',
    '--network', $NetworkName,
    '--network-alias', $ContainerName,
    '-e', "POSTGRES_USER=$DbUser",
    '-e', "POSTGRES_PASSWORD=$DbPassword",
    '-e', "POSTGRES_DB=$DbName",
    '-e', 'PGDATA=/var/lib/postgresql/data/pgdata',
    '-v', $mountArg,
    '-p', "${Port}:5432",
    $Image
)

$containerId = (Invoke-Docker $runArgs) -join ''
if ($script:DockerExit -ne 0) {
    Write-Warn $script:DockerStderr
    throw 'Avvio del container fallito.'
}
Write-Ok "Container avviato ($($containerId.Substring(0, [Math]::Min(12, $containerId.Length))))."

# ---------------------------------------------------------------- 8. Attesa
Write-Step 'Attendo che PostgreSQL sia pronto'

$ready = $false
for ($i = 1; $i -le 60; $i++) {
    Start-Sleep -Seconds 2

    $state = (Invoke-Docker @('inspect', '-f', '{{.State.Running}}', $ContainerName)) -join ''
    if ($state -ne 'true') { break }

    # pg_isready verifica solo che il server risponda: si interroga il db di
    # manutenzione "postgres", che esiste sempre.
    Invoke-Docker @('exec', $ContainerName, 'pg_isready', '-U', $DbUser, '-d', 'postgres') | Out-Null
    if ($script:DockerExit -eq 0) { $ready = $true; break }

    Write-Host '.' -NoNewline
}
Write-Host ''

if (-not $ready) {
    Write-Warn 'PostgreSQL non e'' diventato disponibile. Ultime righe di log:'
    $log = Invoke-Docker @('logs', '--tail', '60', $ContainerName)
    $logText = (@($log) + @($script:DockerStderr)) -join [Environment]::NewLine
    Write-Host $logText

    if ($logText -match 'permission|ownership|chmod|initdb|not permitted') {
        Write-Warn ''
        Write-Warn 'Sembra un problema di permessi tipico dei bind mount Windows.'
        Write-Warn "Rilancia con:  .\$($MyInvocation.MyCommand.Name) -UseVolume -Force"
        Write-Warn "(i dati finiranno in un volume Docker invece che in $DataPath)"
    }
    throw 'Inizializzazione del database non riuscita.'
}

# ---------------------------------------------------------------- 9. Provisioning
<#
    Le variabili POSTGRES_USER / POSTGRES_PASSWORD / POSTGRES_DB vengono lette
    dall'entrypoint dell'immagine SOLO al primo avvio, quando la cartella dati
    e' vuota. Se in $DataPath esiste gia' un cluster (per esempio da un tentativo
    precedente), quelle variabili vengono ignorate in silenzio: il server parte,
    pg_isready risponde OK, ma utente e database potrebbero non esistere.
    Questo blocco li crea comunque, in modo idempotente.
#>
Write-Step 'Provisioning di utente e database'

# Chi puo' amministrare questo cluster? Prima si prova l'utente richiesto,
# poi il superuser di default "postgres".
$adminUser = $null
foreach ($candidate in @($DbUser, 'postgres')) {
    Invoke-Docker @('exec', $ContainerName, 'psql', '-U', $candidate, '-d', 'postgres', '-tAc', 'SELECT 1') | Out-Null
    if ($script:DockerExit -eq 0) { $adminUser = $candidate; break }
}
if (-not $adminUser) {
    Write-Warn $script:DockerStderr
    throw "Impossibile connettersi al cluster, ne' come '$DbUser' ne' come 'postgres'."
}
Write-Ok "Connesso come superuser '$adminUser'."

<#
    Invoke-Psql: passa l'SQL a psql tramite STDIN, non con -c.

    PowerShell 5.1 non preserva le virgolette doppie quando passa un argomento
    a un eseguibile nativo: la stringa
        CREATE DATABASE "emma-db" OWNER "emma";
    arriva a docker.exe come
        CREATE DATABASE emma-db OWNER emma;
    e il server risponde   syntax error at or near "-"   perche' emma-db,
    senza virgolette, non e' un identificatore valido (contiene un trattino).

    Con "docker exec -i" e l'SQL inviato sullo standard input il testo non
    viene mai reinterpretato dalla riga di comando, quindi gli identificatori
    fra virgolette restano intatti.
#>
function Invoke-Psql {
    param([string] $Sql, [string] $Database = 'postgres')

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = $Sql | & docker exec -i $ContainerName psql -U $adminUser -d $Database -v ON_ERROR_STOP=1 -tA 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prev
    }

    $stdout = @()
    $stderr = @()
    foreach ($line in @($raw)) {
        if ($line -is [System.Management.Automation.ErrorRecord]) { $stderr += [string]$line }
        else                                                     { $stdout += [string]$line }
    }

    if ($exit -ne 0) {
        Write-Warn (($stderr + $stdout) -join [Environment]::NewLine)
        throw "Query fallita: $Sql"
    }
    return ($stdout -join '').Trim()
}

# le virgolette singole nella password vanno raddoppiate per SQL
$sqlPassword = $DbPassword -replace "'", "''"

# --- ruolo
$roleExists = Invoke-Psql "SELECT 1 FROM pg_roles WHERE rolname = '$DbUser';"
if ($roleExists -eq '1') {
    Invoke-Psql "ALTER ROLE ""$DbUser"" WITH LOGIN CREATEDB PASSWORD '$sqlPassword';" | Out-Null
    Write-Ok "Utente '$DbUser' gia'' presente: password allineata."
}
else {
    Invoke-Psql "CREATE ROLE ""$DbUser"" WITH LOGIN CREATEDB PASSWORD '$sqlPassword';" | Out-Null
    Write-Ok "Utente '$DbUser' creato."
}

# --- database (CREATE DATABASE non supporta IF NOT EXISTS)
$dbExists = Invoke-Psql "SELECT 1 FROM pg_database WHERE datname = '$DbName';"
if ($dbExists -eq '1') {
    Write-Ok "Database '$DbName' gia'' presente."
}
else {
    Invoke-Psql "CREATE DATABASE ""$DbName"" OWNER ""$DbUser"" ENCODING 'UTF8';" | Out-Null
    Write-Ok "Database '$DbName' creato."
    Write-Warn "Nota: la cartella dati conteneva gia'' un cluster, quindi le variabili"
    Write-Warn "POSTGRES_* dell'immagine erano state ignorate. Ora e'' tutto allineato."
}

Invoke-Psql "GRANT ALL PRIVILEGES ON DATABASE ""$DbName"" TO ""$DbUser"";" | Out-Null
# dal 15 in poi serve anche il permesso esplicito sullo schema public
Invoke-Psql "GRANT ALL ON SCHEMA public TO ""$DbUser"";" $DbName | Out-Null
Invoke-Psql "ALTER SCHEMA public OWNER TO ""$DbUser"";" $DbName | Out-Null
Write-Ok 'Privilegi assegnati.'

# ---------------------------------------------------------------- 9b. pgvector
<#
    CREATE EXTENSION va eseguito da un superuser e va ripetuto su OGNI database
    che usa i vettori: abilitarla su "postgres" non la rende visibile a $DbName.
    Si controlla prima pg_available_extensions per dare un messaggio chiaro
    quando l'immagine in uso non contiene affatto pgvector.
#>
if (-not $SkipVectorExtension) {
    Write-Step 'Abilitazione dell''estensione vector (pgvector)'

    $available = Invoke-Psql "SELECT 1 FROM pg_available_extensions WHERE name = 'vector';" $DbName
    if ($available -ne '1') {
        Write-Warn "L'immagine '$Image' non contiene pgvector."
        Write-Warn "Usa l'immagine pgvector/pgvector:pg$PostgresVersion (default di questo script),"
        Write-Warn 'oppure rilancia con -SkipVectorExtension se non ti serve.'
        throw 'Estensione "vector" non disponibile nel server.'
    }

    Invoke-Psql 'CREATE EXTENSION IF NOT EXISTS vector;' $DbName | Out-Null
    $vectorVersion = Invoke-Psql "SELECT extversion FROM pg_extension WHERE extname = 'vector';" $DbName
    Write-Ok "Estensione vector attiva su '$DbName' (versione $vectorVersion)."
}
else {
    Write-Warn 'Estensione vector saltata (-SkipVectorExtension).'
}

# ---------------------------------------------------------------- 10. Verifica
Write-Step 'Verifica finale'

$check = (Invoke-Docker @('exec', $ContainerName, 'psql', '-U', $DbUser, '-d', $DbName, '-tAc', 'SELECT current_user || '' @ '' || current_database();')) -join ''
if ($script:DockerExit -ne 0) {
    Write-Warn $script:DockerStderr
    throw 'Il database non risponde alle query.'
}
Write-Ok "Risposta dal server: $($check.Trim())"

# ---------------------------------------------------------------- 11. Riepilogo
$storageDesc = if ($UseVolume) { "volume Docker '$VolumeName'" } else { $DataPath }

Write-Host ''
Write-Host '========================================================' -ForegroundColor Green
Write-Host ' PostgreSQL pronto all''uso' -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Green
Write-Host " Database .......... $DbName"
Write-Host " Utente ............ $DbUser"
Write-Host " Password .......... $DbPassword"
Write-Host " Dati salvati in ... $storageDesc"
Write-Host " Immagine .......... $Image"
Write-Host " Container ......... $ContainerName"
Write-Host " Rete Docker ....... $NetworkName"
Write-Host ''
Write-Host ' Da EMMA-SERVER (stessa rete, porta interna 5432):' -ForegroundColor Cyan
Write-Host "   host: $ContainerName    porta: 5432"
Write-Host "   postgresql://${DbUser}:${DbPassword}@${ContainerName}:5432/${DbName}"
Write-Host ''
Write-Host ' Dal PC host (pgAdmin, DBeaver, ...):' -ForegroundColor Cyan
Write-Host "   postgresql://${DbUser}:${DbPassword}@localhost:${Port}/${DbName}"
Write-Host ''
Write-Host ' Comandi utili:'
Write-Host "   docker exec -it $ContainerName psql -U $DbUser -d $DbName"
Write-Host "   docker network connect $NetworkName EMMA-SERVER"
Write-Host "   docker stop $ContainerName"
Write-Host "   docker start $ContainerName"
Write-Host "   docker logs -f $ContainerName"
Write-Host '========================================================' -ForegroundColor Green