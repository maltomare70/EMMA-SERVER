# EmmaServer.Tests

Test di integrazione su `DocService`. Non sono unit test con mock: usano i servizi reali
(`DocService`, `DocRepository`, `FornitoriService`, `ArticoliService`, `LogService`) e **creano
davvero le bolle sulla tabella `docs`**.

Le uniche due sostituzioni rispetto alla produzione sono:

| In produzione | Nei test |
|---|---|
| `UserConnectionProvider` (tenant dai claim dell'HttpContext) | `TestUserConnectionProvider` (tenant e connessione da configurazione) |
| `HttpClient` verso il servizio EMMA-AI | `StubHttpMessageHandler`, la risposta la decide il test |

## Configurazione

La catena di configurazione, dalla priorita' piu' bassa alla piu' alta:

1. `EmmaServer/appsettings.Development.json` (trovato risalendo dalla cartella di output) — evita di
   duplicare le credenziali: di default i test puntano allo stesso database del server;
2. `appsettings.Tests.json` di questo progetto;
3. variabili d'ambiente con prefisso `EMMA_` e `__` come separatore di sezione.

Esempio per puntare a un Postgres locale invece che a quello di sviluppo:

```powershell
$env:EMMA_Database__Host     = "localhost"
$env:EMMA_Database__Database = "emma"
$env:EMMA_Database__UserName = "postgres"
$env:EMMA_Database__Password = "postgres"
$env:EMMA_Database__SslMode  = "Disable"
$env:EMMA_Test__Tenant       = "test-locale"

dotnet test
```

Se il database non e' configurato o non risponde i test **vengono saltati**, non falliti
(vedi `IntegrationFactAttribute` / `TestDatabase`).

## Dati creati

- Tutte le bolle vengono create sotto il tenant di test (`Test:Tenant`, default `test-xunit`),
  quindi restano separate dai dati reali.
- **I documenti non vengono cancellati a fine test**: servono anche a popolare il database.
  Per ripulire:

```sql
DELETE FROM docs      WHERE tenant = 'test-xunit';
DELETE FROM articoli  WHERE tenant = 'test-xunit';
DELETE FROM fornitori WHERE tenant = 'test-xunit';
DELETE FROM log       WHERE tenant = 'test-xunit';
```

- Ogni test usa un numero bolla univoco (`BollaFactory.NumeroBollaUnivoco`), quindi due esecuzioni
  consecutive non si sovrappongono.

## Cosa coprono i test

`DocServiceBolleTests`

- creazione della bolla e rilettura del jsonb `content`;
- salvataggio dell'allegato;
- creazione di piu' bolle distinte;
- documento gia' presente e **aperto** → viene sostituito;
- documento gia' presente e **chiuso** → eccezione;
- `Stato = -1` = tutti gli stati, `Stato = 0` = solo aperti;
- filtri su mittente e numero **case-insensitive** (il case nel jsonb non e' normalizzato);
- `CambiaTipoAsync` e `DeleteDocAsync`.

`DocServiceRigheTests`

- inserimento, modifica e cancellazione delle righe articolo dentro il jsonb;
- un test di regressione **disattivato (`Skip`)** che documenta un bug noto: le SELECT di
  `InsertRigaDocAsync` / `UpdateRigaDocAsync` / `DeleteRigaDocAsync` non leggono la colonna
  `allegato`, ma poi Dapper.Contrib `UpdateAsync` riscrive tutte le colonne e l'allegato viene
  azzerato. Togliere lo `Skip` dopo aver aggiunto `allegato` a quelle SELECT.

`DocServiceImportTests`

- `ImportDocAsync` con risposta AI valida (stub HTTP) → bolla sul database;
- `ImportDocAsync` con errore del servizio AI → `ApplicationException`;
- `ImportDocAsync` con un DDT (tipo 2) → aggiorna anche l'anagrafica fornitori;
- `ImportFatturaElettronicaAsync` con un XML FatturaPA (FPR12) → fattura sul database.

## Come lanciarli

```powershell
dotnet test EmmaServer.Tests\EmmaServer.Tests.csproj
```

Solo un gruppo:

```powershell
dotnet test --filter "FullyQualifiedName~DocServiceBolleTests"
```
