# Modulo Anomalie — prezzi e quantità

Rilevare, al momento dell'import di un DDT o di una fattura, le righe che non tornano
rispetto allo storico del tenant, e metterle davanti all'operatore in ordine di gravità.

> Nota onesta sul machine learning: il grosso del valore di questo modulo si ottiene
> **senza** apprendimento supervisionato. Qui il ML serve in due punti precisi — il
> rilevamento multivariato dove la statistica non arriva, e (con AutoML) il filtro dei
> falsi positivi addestrato sul feedback dell'operatore. Tutto il resto sono regole e
> statistica robusta, che hanno il vantaggio decisivo di essere **spiegabili**: un avviso
> che l'operatore non sa perché è comparso viene ignorato, e dopo due settimane nessuno
> guarda più la lista.

---

## 1. I tre livelli

| Livello | Cosa fa | Tecnica | Serve storico? |
| --- | --- | --- | --- |
| 1 | Coerenza interna del documento | regole deterministiche | no |
| 2 | Scostamento dal comportamento abituale di (fornitore, articolo) | mediana + MAD, z robusto | ≥ 5 osservazioni |
| 3a | Righe strane per combinazione di fattori | ML.NET Randomized PCA | ~200 righe per tenant |
| 3b | Cambio di regime sul prezzo | ML.NET SSA / SR-CNN | ≥ 30 osservazioni per articolo |
| 3c | "Questo avviso vale la pena mostrarlo?" | **AutoML** classificazione binaria | feedback operatore |

L'ordine è anche l'ordine di implementazione. Il livello 1 va in produzione da solo e
porta già valore; il 3c arriva mesi dopo, ma le colonne che lo alimentano vanno
progettate subito.

---

## 2. I dati: da dove escono

Lo storico non sta in `bolle_rows`, sta nel JSONB di `docs`
(`content->'document'->'articoli'`, riempito sia da `ImportDocAsync` che da
`ImportFatturaElettronicaAsync`).

```sql
SELECT d.id                                        AS doc_id,
       d.tenant,
       d.data_creazione,
       d.content->'document'->>'mittente'          AS mittente,
       d.content->'document'->>'data_bolla'        AS data_bolla,
       d.content->'document'->>'tipo_documento'    AS tipo_doc,   -- '2' DDT, '4' fattura
       r->>'id_riga'                               AS id_riga,
       r->>'codice'                                AS codice,
       r->>'descrizione'                           AS descrizione,
       r->>'unita_misura'                          AS um,
       (r->>'quantita')::numeric                   AS qta,
       (r->>'imponibile')::numeric                 AS imponibile,
       (r->>'totale')::numeric                     AS totale
FROM   docs d,
       LATERAL jsonb_array_elements(d.content->'document'->'articoli') AS r
WHERE  d.tenant = @Tenant
  AND  d.content->'document'->>'tipo_documento' IN ('2','4');
```

**Prezzo unitario.** Non è memorizzato: va calcolato, e con una precauzione.
`ImportFatturaElettronicaAsync` scrive `Imponibile = 0` e mette il prezzo di riga in
`Totale`; i DDT che arrivano da EMMA-AI possono avere l'imponibile valorizzato e il
totale comprensivo di IVA. Quindi:

```sql
prezzo_unitario = COALESCE(NULLIF(imponibile,0), NULLIF(totale,0)) / NULLIF(qta,0)
```

e i due tipi documento **non si mescolano nella stessa baseline**: il prezzo di un DDT e
quello di una fattura elettronica non sono la stessa grandezza. Una baseline per
`tipo_doc`, oppure solo sui DDT.

**Chiave dello storico.** `(tenant, id_fornitore, codice)` normalizzato — meglio ancora
`articoli.rifcodice` quando è valorizzato, così i codici diversi che l'operatore ha già
ricondotto allo stesso articolo confluiscono in una serie sola invece che in tre serie
troppo corte.

**Non scansionare il JSONB a ogni import.** Su `docs` non ci sono indici sui campi riga:
la query sopra è un seq scan con esplosione dell'array. Si materializza una baseline di
notte (§4) e in linea si legge solo quella.

---

## 3. Livello 1 — regole, nessun modello

Girano su un documento appena importato, senza storico.

| Controllo | Condizione |
| --- | --- |
| Quadratura righe | `abs(SUM(righe.totale) - document.totale) > 0.01 + sconto` |
| Quadratura riga | `abs(totale - qta * prezzo_unitario) > 0.01` |
| Valori impossibili | `qta <= 0`, prezzo `<= 0`, `codice` vuoto, `unita_misura` vuota |
| Aliquota IVA | non in `{0, 4, 5, 10, 22}` |
| Data | `data_bolla` nel futuro, o più vecchia di 18 mesi |
| Righe gemelle | stesso `codice` due volte nello stesso documento con quantità diverse |
| Duplicato | stesso `(fornitore, numero_bolla, data)` — `AddDocAsync` già lo intercetta; qui si aggiunge lo stesso *contenuto* con numero diverso, via hash delle righe ordinate |

Questo livello ha una seconda funzione, più importante della prima: **fa da guardia ai
livelli successivi**. Se la quadratura fallisce, l'estrazione è sbagliata e i prezzi di
quel documento non devono né generare avvisi né entrare nella baseline — altrimenti un
solo DDT letto male avvelena lo storico dell'articolo per mesi.

---

## 4. Livello 2 — z robusto su mediana e MAD

Per ogni `(tenant, id_fornitore, codice)` sugli ultimi 24 mesi:

```
mediana = median(prezzo_unitario)
MAD     = median(|prezzo_unitario - mediana|)
z       = 0.6745 * (x - mediana) / MAD          → sospetto se |z| > 3.5
```

Mediana e MAD e non media e deviazione standard: con la media, un'unica riga letta male
da 10.000 € sposta il centro e nasconde per sempre gli scostamenti veri.

Tre casi da trattare a mano, altrimenti il modulo fa più rumore che servizio:

- **`MAD = 0`** (prezzo sempre identico, il caso più comune sui listini): la formula
  divide per zero. Regola dedicata: qualsiasi scostamento oltre l'1% è un avviso, e il
  messaggio è "prezzo fisso a 12,40 € sulle ultime 14 bolle, questa riporta 13,10 €".
- **`n < 5`**: niente livello 2, si passa al 3a.
- **Unità di misura cambiata** rispetto alla UM prevalente dell'articolo (PZ → CT, o
  KG → q): è la prima causa di falso allarme sul prezzo. Se la UM cambia, il confronto
  di prezzo si **sospende** e si emette invece un avviso di tipo `um_cambiata`.

Stessa struttura per le quantità, ma su `log(qta)`: le quantità sono asimmetriche
(tanti ordini piccoli, pochi grandi) e su scala lineare il MAD segnalerebbe ogni
riordino grosso.

Il messaggio all'operatore è già scritto dai numeri:
`"ARTICOLO X — mediana 12,40 €/PZ su 14 bolle, questa bolla 19,90 €/PZ (+60%)"`.

---

## 5. Livello 3 — dove entra ML.NET

### 3a. Righe anomale per combinazione di fattori — `RandomizedPca`

Non supervisionato: si addestra sulle righe considerate normali (quelle senza avvisi di
livello 1 e 2) e assegna a ogni nuova riga un punteggio di anomalia.

```csharp
var pipeline = ml.Transforms.Concatenate("Features", featureCols)
    .Append(ml.Transforms.NormalizeMeanVariance("Features"))
    .Append(ml.AnomalyDetection.Trainers.RandomizedPca(
        featureColumnName: "Features", rank: 6, ensureZeroMean: true));
```

Feature per riga:

- `log(prezzo_unitario)` e scostamento % dalla mediana del fornitore sulla stessa famiglia di articoli
- `log(qta)`
- quota della riga sul totale documento
- numero di righe del documento
- giorni dall'ultimo documento dello stesso fornitore
- quante volte quell'articolo è già comparso nello storico
- aliquota IVA, UM (one-hot)
- mese e giorno della settimana

Soglia: il 99° percentile dei punteggi dello storico del tenant, ricalcolata a ogni
addestramento. Una soglia assoluta non è trasferibile fra tenant.

Serve per i casi che il livello 2 non può vedere: articolo nuovo, prezzo plausibile in
assoluto ma non per quel fornitore in quel periodo, riga che pesa il 90% di un documento
che di solito ne ha trenta equilibrate.

### 3b. Cambio di regime sul prezzo — `Microsoft.ML.TimeSeries`

Per gli articoli con almeno 30 osservazioni, sulla serie dei prezzi:

```csharp
ml.Transforms.DetectSpikeBySsa(
    outputColumnName: "Prediction", inputColumnName: "Prezzo",
    confidence: 95d, pvalueHistoryLength: 12, trainingWindowSize: 40, seasonalityWindowSize: 6);
// oppure DetectChangePointBySsa per il gradino di listino
```

Il punto non è trovare il picco — quello lo fa già il livello 2 — ma **distinguere lo
spike dal gradino**: un errore di battitura è un picco isolato e va segnalato; un
aumento di listino è un gradino permanente, e va segnalato **una volta sola**, non
quaranta volte a ogni bolla successiva. Senza questa distinzione il modulo diventa
inutilizzabile al primo rincaro di un fornitore importante.

Quando il modello rileva un gradino, la baseline dell'articolo si ricalcola sulla nuova
finestra e si scrive un avviso di tipo `nuovo_listino`, severità bassa, informativo.

### 3c. Il filtro dei falsi positivi — qui sta l'AutoML

Appena l'operatore comincia a marcare gli avvisi come *confermato* o *ignorato*, quelle
sono etichette, ed è esattamente il problema per cui AutoML è nato: tabellare,
binario, con feature eterogenee.

```csharp
var experiment = ml.Auto().CreateBinaryClassificationExperiment(
    new BinaryExperimentSettings {
        MaxExperimentTimeInSeconds = 600,
        OptimizingMetric = BinaryClassificationMetric.AreaUnderPrecisionRecallCurve
    });
var result = experiment.Execute(trainData, labelColumnName: "Confermata");
```

Feature: **tutti i punteggi dei livelli precedenti** (z del prezzo, z della quantità,
punteggio PCA, esito delle regole) più il contesto — fornitore, famiglia articolo,
numero di documenti già visti da quel fornitore, storico di conferme/rifiuti su quel
fornitore e su quell'articolo.

Il modello impara cose che non scriveresti mai a mano: che il fornitore X ha prezzi
ballerini per natura e non vale la pena disturbare sotto il 40%, mentre per il fornitore
Y anche il 5% è una segnalazione buona. È ciò che tiene il modulo vivo dopo il terzo
mese.

Metrica da ottimizzare: **AUPRC, non accuracy**. Le anomalie vere sono una piccola
percentuale; un modello che dice sempre "non è un'anomalia" ha il 97% di accuratezza ed
è inutile.

---

## 6. Schema database

```sql
CREATE TABLE anomalie (
    id              bigserial PRIMARY KEY,
    tenant          varchar(100) NOT NULL,
    doc_id          int NOT NULL REFERENCES docs(id) ON DELETE CASCADE,
    id_master       varchar(100),
    id_riga         varchar(100),
    id_fornitore    int,
    codice          varchar(256),
    tipo            varchar(40)  NOT NULL,   -- prezzo_alto | prezzo_basso | qta_anomala
                                             -- um_cambiata | incoerenza_totali | duplicato
                                             -- riga_anomala | nuovo_listino
    livello         smallint     NOT NULL,   -- 1 regola, 2 statistica, 3 modello
    score           double precision,        -- z robusto oppure punteggio del modello
    severita        smallint     NOT NULL,   -- 0 ombra (invisibile), 1..3 visibile
    valore          numeric,
    atteso          numeric,
    delta_perc      numeric,
    messaggio       text,                    -- frase pronta, in italiano, per l'operatore
    modello_ver     varchar(40),             -- quale versione del modello l'ha emessa
    stato           smallint DEFAULT 0,      -- 0 aperta, 1 confermata, 2 ignorata
    utente_feedback varchar(100),
    data_feedback   timestamptz,
    data_creazione  timestamptz DEFAULT now()
);
CREATE INDEX ix_anomalie_tenant_stato ON anomalie(tenant, stato, severita DESC, data_creazione DESC);
CREATE INDEX ix_anomalie_doc          ON anomalie(doc_id);

CREATE TABLE anomalie_baseline (
    id              bigserial PRIMARY KEY,
    tenant          varchar(100) NOT NULL,
    id_fornitore    int          NOT NULL,
    codice          varchar(256) NOT NULL,
    tipo_doc        smallint     NOT NULL,
    n               int          NOT NULL,
    mediana_prezzo  numeric, mad_prezzo numeric,
    mediana_qta     numeric, mad_qta    numeric,
    um_prevalente   varchar(20),
    primo_visto     date, ultimo_visto date,
    aggiornata_il   timestamptz DEFAULT now(),
    UNIQUE (tenant, id_fornitore, codice, tipo_doc)
);

CREATE TABLE ml_models (
    id             bigserial PRIMARY KEY,
    tenant         varchar(100),             -- NULL = modello globale
    nome           varchar(80) NOT NULL,     -- anomalie_pca | anomalie_filtro
    versione       varchar(40) NOT NULL,
    modello        bytea NOT NULL,           -- lo .zip di ML.NET
    metriche       jsonb,
    attivo         boolean DEFAULT false,
    data_creazione timestamptz DEFAULT now(),
    UNIQUE (tenant, nome, versione)
);
```

`stato` + `utente_feedback` **sono** il training set del livello 3c. Vanno messi dal
primo giorno anche se il modello arriverà fra tre mesi: sono dati che non si recuperano
a posteriori.

---

## 7. Innesto nel codice esistente

**Servizio.** `EmmaServer/Services/AnomalieService.cs`, `IAnomalieService`, registrato
`Scoped` in `Program.cs` accanto agli altri. Se la parte ML cresce, si stacca in un
progetto `Emma.ML` a fianco di `Emma.Batches`, ma non serve partire così.

**Punto di chiamata.** In `DocService.ImportDocAsync`, subito dopo
`await AggiornaAnagrafiche(newDoc.id)`:

```csharp
try { await _anomalieService.AnalizzaDocumentoAsync(newDoc.id); }
catch (Exception ex) { Console.WriteLine($"Anomalie non calcolate: {ex.Message}"); }
```

Il try/catch non è pigrizia: **un'anomalia non rilevata è meglio di un DDT rifiutato**.
Stesso innesto in `ImportFatturaElettronicaAsync`, che oggi non chiama nemmeno
`AggiornaAnagrafiche` (riga commentata) — e senza fornitore/articoli allineati la
baseline della fattura non si aggancia a niente: va sistemato prima.

**Endpoint** — nuovo `AnomalieEndpoints.cs` + `app.MapAnomalieRoutes()`:

| Metodo | Rotta | Uso |
| --- | --- | --- |
| GET | `/api/v1/anomalie` | lista del tenant, filtri `stato`, `severita`, `fornitore`, `da`/`a` |
| GET | `/api/v1/anomalie/doc/{docId}` | avvisi di un documento, per il badge nella pagina di dettaglio |
| POST | `/api/v1/anomalie/{id}/feedback` | `{ stato: 1|2, note }` — è la raccolta delle etichette |
| GET | `/api/v1/anomalie/statistiche` | precisione degli avvisi per tipo, per capire cosa sta funzionando |

**Batch notturno.** Nuovo `AnomalieBackgroundService` accanto a
`ImportDocBackgroundService` e `CleanDataBackgroundService`: ricalcolo baseline →
riaddestramento PCA → riaddestramento filtro AutoML se ci sono ≥ 200 nuove etichette.

**Inferenza.** `PredictionEnginePool` registrato singleton: `PredictionEngine` **non è
thread-safe**, e in un endpoint concorrente l'errore si manifesta come risultati
sbagliati, non come eccezione.

**NuGet:** `Microsoft.ML`, `Microsoft.ML.TimeSeries`, `Microsoft.ML.AutoML`. Tutti
managed, nessuna dipendenza nativa problematica nel container Linux del Dockerfile.

**Multi-tenant.** Baseline **sempre** per tenant: i prezzi di un tenant non dicono nulla
su un altro. Il modello del livello 3c invece **globale con `tenant` come feature
categorica**, perché il feedback è scarso e un modello per tenant non si addestrerebbe
mai.

---

## 8. Ordine di lavoro, e come capire se funziona

| Fase | Contenuto | Valore |
| --- | --- | --- |
| 1 | Tabelle, livello 1, endpoint, lista in EMMA-WEB | immediato, zero ML — **fatti tabelle, livello 1 ed endpoint; manca la lista in EMMA-WEB** |
| 2 | Baseline notturna + livello 2 | copre l'80% dei casi reali |
| 3 | Livelli 3a/3b in **ombra** (`severita = 0`, scritti ma non mostrati) | si misura senza disturbare nessuno |
| 4 | AutoML sul feedback raccolto nelle fasi 1-3 | abbatte i falsi positivi |

### Stato della fase 2 (implementata)

| Pezzo | Dove |
| --- | --- |
| Tabelle `anomalie`, `anomalie_baseline` | `Migrations/sql_003.sql`, presa in automatico da `EmmaRepository.InitializeAsync` |
| Mediana / MAD / z robusto | `Services/Anomalie/StatisticaRobusta.cs` |
| Prezzo unitario, normalizzazioni, guardia di quadratura provvisoria | `Services/Anomalie/RigheDocumento.cs` |
| Aggancio fornitore (esatto, poi FuzzySharp ≥ 90) e `rifcodice`, in sola lettura | `Services/Anomalie/RisolutoreAnagrafiche.cs` |
| Calcolo baseline per (tenant, fornitore, articolo, tipo_doc) | `Services/Anomalie/CostruttoreBaseline.cs` |
| Regole del livello 2 e messaggi | `Services/Anomalie/ValutatoreLivello2.cs` |
| Orchestrazione | `Services/AnomalieService.cs`, `Repositories/AnomalieRepository.cs` |
| Batch notturno | `Background/AnomalieBackgroundService.cs` (ora in `Anomalie:OraUtc`) |
| Innesto | `DocService.ImportDocAsync` e `ImportFatturaElettronicaAsync`, in try/catch |
| Rotte | `Endpoints/AnomalieEndpoints.cs`, vedi "Endpoint" sotto |

Scelte prese in implementazione:

- `mediana_qta` e `mad_qta` sono salvate **in scala logaritmica** (mediana e MAD di `ln(qta)`).
- La mediana del prezzo usa solo le righe con l'UM prevalente; `n` conta solo quelle.
- Oltre allo z > 3,5 serve uno scostamento di almeno il 2% (`ScostamentoMinimoPrezzo`): con MAD
  piccolissimi uno z alto può valere mezzo centesimo.
- Quantità con MAD = 0: avviso solo oltre il triplo o sotto un terzo (`RapportoQtaFissa`).
- Severità del prezzo: < 10% → 1, < 30% → 2, oltre → 3. Quantità: 1, oppure 2 se |z| > 10.
- Escluse dalla baseline le righe con un avviso **confermato** dall'operatore e documenti e righe con
  avvisi di livello 1 bloccanti non ignorati: il feedback ripulisce lo storico.
- La quadratura è tollerante (somma righe uguale al totale con o senza IVA, oppure all'imponibile)
  e vale anche per lo storico mai passato dal livello 1. `Anomalie:RichiediQuadratura = false` fa sì
  che un documento che non quadra venga segnalato ma non blocchi il livello 2.
- Il batch non passa dalle API HTTP come gli altri: `AnomalieRepository` non deriva da
  `RepositoryGenerico` (il cui costruttore chiama `GetTenant()`), quindi gira senza HttpContext.

### Endpoint (implementati)

Tutti richiedono l'autenticazione e lavorano sul tenant del claim `tenant`.

| Metodo | Rotta | Uso |
| --- | --- | --- |
| GET | `/api/v1/anomalie` | lista paginata; filtri `stato`, `severita` (**minima**, default 1: l'ombra non si vede), `fornitore` (id), `da`/`a` (data dell'avviso, `a` incluso), `livello`, `tipo`, `docId`, `limit` (default 100, max 500), `offset`. Risposta `{ totale, limit, offset, righe }`; ogni riga porta anche mittente, numero, data e tipo del documento e la descrizione del fornitore |
| POST | `/api/v1/anomalie/{id}/feedback` | `{ "stato": 1 \| 2, "note": "..." }`; `stato: 0` riapre e cancella il feedback. L'utente viene dal login. Restituisce l'avviso aggiornato |
| GET | `/api/v1/anomalie/statistiche` | `da`, `a`, `livello`. Per tipo e complessivo: totali, aperte, confermate, ignorate, `precisione` = confermate / (confermate + ignorate), `sotto_soglia` se < 30% con almeno 10 feedback. Contano solo gli avvisi visibili (severità > 0) |
| GET | `/api/v1/anomalie/doc/{docId}` | avvisi di un documento, per il badge nella pagina di dettaglio |
| POST | `/api/v1/anomalie/doc/{docId}/analizza` | rilancia livello 1 e 2 sul documento |
| POST | `/api/v1/anomalie/baseline` | ricalcolo manuale della baseline (admin: tutti i tenant) |

Il feedback su un avviso **bloccante** di livello 1 (quadratura, duplicato, riga incoerente, valore
impossibile) rilancia subito l'analisi del documento: ignorare una quadratura sblocca il confronto
dei prezzi. La rianalisi riscrive gli avvisi ancora aperti di quel documento, che prendono un nuovo
`id`: dopo un feedback del genere la lista in EMMA-WEB va ricaricata.

La colonna `note` è aggiunta a `anomalie` da `sql_003.sql` con `ADD COLUMN IF NOT EXISTS`.

### Stato del livello 1 (implementato)

Le regole stanno in `Services/Anomalie/ValutatoreLivello1.cs` e girano in `AnalizzaDocumentoAsync`
**prima** del livello 2 (tipi e severità):

| Controllo | Tipo | Sev. | Blocca |
| --- | --- | --- | --- |
| Somma righe ≠ totale documento, o documento senza righe | `incoerenza_totali` | 3 | documento |
| Stesso contenuto (impronta SHA-256 delle righe) di un altro documento dello stesso mittente, numero diverso, ultimi 12 mesi | `duplicato` | 3 | documento |
| Totale riga che non torna con imponibile e quantità in nessuna lettura (imponibile di riga o unitario, con o senza IVA) | `riga_incoerente` | 2 | riga |
| Quantità ≤ 0, prezzo mancante, codice o UM vuoti (un solo avviso per riga) | `valore_impossibile` | 2 (1 se solo codice/UM) | riga |
| Aliquota IVA fuori da {0, 4, 5, 10, 22} o non leggibile | `aliquota_iva` | 1 | no |
| Data mancante/illeggibile o nel futuro (sev. 2), più vecchia di 18 mesi (sev. 1) | `data_anomala` | 1-2 | no |
| Stesso codice più volte con quantità diverse | `righe_gemelle` | 1 | no |

Adattamenti rispetto al par. 3:

- **DDT senza prezzi**: se nessuna riga ha importi, il prezzo mancante non è un errore e non si segnala.
- **Fatture elettroniche**: codice articolo e UM sono facoltativi in FatturaPA, non si segnalano;
  una riga a prezzo zero ha severità 1 (omaggi, righe descrittive).
- "Blocca" guarda lo **stato salvato**: un avviso che l'operatore ha ignorato (`stato = 2`) non blocca
  più né il livello 2 né la baseline.
- La quadratura riga del par. 3 (`totale = qta × prezzo`) non era applicabile così com'è, perché il
  prezzo unitario è derivato proprio da quei campi: si verifica invece la coerenza fra totale,
  imponibile e quantità.

La modalità ombra della fase 3 è quella che rende sensata la fase 4: per un mese il
modello predice e si registra, accanto, quello che l'operatore ha deciso davvero. Quando
i due concordano si accende l'interruttore — e nel frattempo hai raccolto gratis il
dataset di addestramento.

**La metrica da difendere** è la precisione degli avvisi mostrati:
`confermate / (confermate + ignorate)`, interrogabile direttamente sulla tabella
`anomalie`. Sotto il 30% l'operatore smette di guardare la lista e il modulo è morto,
per quanto sofisticato sia il modello dietro. AUC e F1 servono a scegliere fra due
modelli; questa serve a sapere se il modulo è vivo.

---

## 9. Tre errori da non fare

1. **Generare avvisi su documenti che non quadrano.** Prima si corregge l'estrazione, poi
   si parla di anomalie. Un documento che fallisce il livello 1 non entra nemmeno nella
   baseline.
2. **Usare media e deviazione standard.** Un solo valore letto male sposta il centro e
   nasconde per sempre gli scostamenti veri di quell'articolo.
3. **Partire dal modello.** Il livello 1 e il livello 2 coprono la gran parte dei casi
   reali, sono spiegabili all'utente e non hanno bisogno di essere riaddestrati. Il
   machine learning serve dove quelli non arrivano — e per decidere cosa vale la pena
   mostrare.
