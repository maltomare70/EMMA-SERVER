# EmmaServer.DataSeed.Tests

Progetto di **generazione dati**, non di verifica: i "test" servono solo a creare documenti sul
database, per avere qualcosa su cui lavorare (conciliazione, anomalie, UI).

I servizi usati sono quelli veri di EmmaServer (`DocService`, `DocRepository`, `FornitoriService`,
`ArticoliService`, ...). L'unica sostituzione e' `IUserConnectionProvider`, che in produzione legge
il tenant dai claim dell'HttpContext: qui tenant e connessione arrivano dalla configurazione.

## Cosa genera

Un solo mittente (`Cartotecnica Demo Srl`) e un catalogo di tre righe, sempre con le stesse
quantita' e gli stessi prezzi:

| Codice | Descrizione | Qta | UM | Prezzo unitario | Importo riga |
|---|---|---:|---|---:|---:|
| ART-001 | Carta A4 80 g - risma 500 fogli | 10 | PZ | 4,50 | 45,00 |
| ART-002 | Toner nero compatibile | 3 | PZ | 38,00 | 114,00 |
| ART-003 | Cartone imballo 60x40x40 | 25 | PZ | 1,20 | 30,00 |

### `GeneraDocumentiParzialiTests` (attivo)

Documenti "a scalare": cambia solo QUANTE righe ci sono, cosi' la conciliazione
ordine/bolla/fattura ha righe che matchano e righe che restano scoperte.

| Test | Documento | Tipo | Numero | Righe | Imponibile | Totale |
|---|---|---|---|---:|---:|---:|
| `Ordine_3Righe` | Ordine | `1` | `ORD-<suffisso>` | 3 | 189,00 | 230,58 |
| `Bolla_2Righe` | Bolla (DDT) | `2` | `DDT-<suffisso>` | 2 | 159,00 | 193,98 |
| `Fattura_1Riga` | Fattura | `4` | `FT-<suffisso>` | 1 | 45,00 | 54,90 |

### `GeneraDocumentiTests` (disattivato)

La versione precedente: le stesse tre righe su tutti e tre i documenti. Tutti i test hanno lo
`Skip`; per rigenerare quella terna basta toglierlo.

### Il suffisso del numero documento

Il `<suffisso>` e' condiviso dai documenti della stessa esecuzione (`SeedFixture.SuffissoEsecuzione`),
cosi' la terna si riconosce a colpo d'occhio e due esecuzioni consecutive non si sovrappongono.

Lanciando i test **uno alla volta** ogni esecuzione avrebbe pero' un suffisso diverso: per tenere
legati i tre documenti, fissarlo prima di lanciarli.

```powershell
$env:EMMA_Seed__Suffisso = "0001"
```

Attenzione: con il suffisso fisso il numero documento non cambia piu', quindi rilanciando lo stesso
test il documento precedente viene sostituito se e' ancora aperto (`stato = 0`) e fa eccezione se e'
gia' chiuso - e' la regola di `DocService.AddDocAsync`. Per una nuova terna, cambiare suffisso.

Convenzione sugli importi, la stessa di `ImportFatturaElettronicaAsync`: nella riga
`imponibile = 0` e l'importo netto sta in `totale`; nel documento `imponibile` e' la somma netta e
`totale` e' netto + IVA. Cosi' `RigheDocumento.PrezzoUnitario` ricava il prezzo giusto e
`RigheDocumento.DocumentoQuadra` considera il documento quadrato.

Dopo ogni documento viene chiamato `AddOrUpdateFornitorieArticoli`, come fa l'import reale: popola
`fornitori` sempre e `articoli` solo per il DDT (tipo 2) - e' la regola di `ArticoliService`.

## Configurazione

Come per `EmmaServer.Tests`, dalla priorita' piu' bassa alla piu' alta:

1. `EmmaServer/appsettings.Development.json` (trovato risalendo dalla cartella di output);
2. `appsettings.DataSeed.json` di questo progetto;
3. variabili d'ambiente con prefisso `EMMA_` e `__` come separatore.

Il tenant si imposta con `Seed:Tenant` (default `Test`), il suffisso con `Seed:Suffisso`:

```powershell
$env:EMMA_Seed__Tenant   = "Test"
$env:EMMA_Seed__Suffisso = "0001"
$env:EMMA_Database__Host = "localhost:5432"
```

Se il database non e' configurato o non risponde i test **vengono saltati**, non falliti
(`SeedFactAttribute` / `SeedDatabase`).

## Come si lancia

Un documento alla volta (e' il modo previsto):

```powershell
$env:EMMA_Seed__Suffisso = "0001"

dotnet test EmmaServer.DataSeed.Tests\EmmaServer.DataSeed.Tests.csproj --filter "FullyQualifiedName~Ordine_3Righe"
dotnet test EmmaServer.DataSeed.Tests\EmmaServer.DataSeed.Tests.csproj --filter "FullyQualifiedName~Bolla_2Righe"
dotnet test EmmaServer.DataSeed.Tests\EmmaServer.DataSeed.Tests.csproj --filter "FullyQualifiedName~Fattura_1Riga"
```

Tutti e tre in un colpo solo (stesso suffisso anche senza configurarlo):

```powershell
dotnet test EmmaServer.DataSeed.Tests\EmmaServer.DataSeed.Tests.csproj
```

Numero documento, righe e importi finiscono nell'output del test (`ITestOutputHelper`).

## Come si estende

- altre righe o altri importi: `DocumentoFactory.CreaDocumento(tipo, suffisso, data, mittente, righe)`
  accetta mittente e righe (`RigaDemo`) su misura; `DocumentoFactory.PrimeRighe(n)` prende le prime
  n righe del catalogo;
- piu' documenti in un colpo solo: un ciclo dentro un nuovo `[SeedFact]` che chiama
  `_fixture.SalvaDocumentoAsync(...)`;
- altri tipi: i codici sono in `TipoDocEnum` (1 ordine, 2 DDT, 3 fattura accompagnatoria,
  4 fattura, 5 nota di accredito).

## Pulizia

I documenti **non** vengono cancellati a fine esecuzione: e' il loro scopo. Per ripulire:

```sql
delete from fornitori where "tenant" = 'test';
delete from articoli where "tenant" = 'test';
delete from anomalie where "tenant" = 'test';
delete from anomalie_baseline where "tenant" = 'test';
delete from conciliarighe where "tenant" = 'test';
delete from docs where "tenant" = 'test';
delete from log where "tenant" = 'test';
delete from rag_documents where "tenant" = 'test';
delete from rag_chunks where "tenant" = 'test';
```
