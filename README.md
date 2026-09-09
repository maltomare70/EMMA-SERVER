# EMMA-SERVER

## Database vettoriale sui PDF (`/api/v1/document`)

Pipeline di indicizzazione, tutta dentro `EmmaServer`:

```
PDF --PdfPig + NFKC--> testo per pagina, legature tipografiche sciolte
    --Microsoft.ML.Tokenizers--> chunk strutturali (titoli e paragrafi), max 512 token
    --API Gemini (chiamata diretta)--> vettori a 768 dimensioni, normalizzati L2
    --Npgsql/Dapper--> rag_documents + rag_chunks (pgvector HNSW + full-text italiano)
```

L'embedding non passa da EMMA-AI: `GeminiEmbeddingClient` chiama direttamente
`generativelanguage.googleapis.com`. EMMA-AI resta in gioco solo per l'estrazione
dei DDT su `/api/v1/doc`.

### Prerequisito: schema del database

Da eseguire una volta sul database `emma`:

```bash
psql "$CONN" -f sql/rag.sql          # installazione nuova
psql "$CONN" -f sql/rag_fulltext.sql # su un database gia' creato prima della ricerca ibrida
```

`rag.sql` abilita l'estensione `vector`, crea `rag_documents` e `rag_chunks`,
l'indice HNSW `vector_cosine_ops` e la colonna `tsv` per la ricerca lessicale.
`rag_fulltext.sql` aggiunge solo `tsv` e il suo indice GIN, per chi ha gia'
il database creato con la versione precedente dello script. Se non si vuole
eseguire la migrazione, mettere `Rag:HybridSearch` a `false`.

### Endpoint

| Metodo | Rotta | Descrizione |
| --- | --- | --- |
| POST | `/api/v1/document` | Upload multipart di un PDF: estrae, spezza, vettorizza e salva. Risponde con `DocumentResponse`. |
| POST | `/api/v1/document/search` | Ricerca semantica (`query`, `top_k`, `doc_id`, `min_score`) sui chunk del tenant. |
| GET | `/api/v1/document` | Elenco dei documenti indicizzati del tenant. |
| GET | `/api/v1/document/{id}/file` | Restituisce il PDF originale (colonna `rag_documents.allegato`). |
| GET | `/api/v1/document/{id}/chunks` | Diagnostica: chunk salvati con testo, pagina, token, dimensioni e norma del vettore. |
| DELETE | `/api/v1/document/{id}` | Cancella documento e chunk (ON DELETE CASCADE). |

Lo stesso file caricato due volte non viene reindicizzato: `rag_documents`
ha un vincolo di unicita' su `(tenant, file_hash)` con l'hash SHA-256 del PDF,
e la risposta torna con `already_indexed: true`.

### Configurazione

```jsonc
"Rag": {
  "ChunkSize": 512,      // token per chunk
  "ChunkOverlap": 64,    // token di sovrapposizione fra chunk consecutivi
  "EmbeddingDim": 768,   // deve combaciare con vector(768) in sql/rag.sql
  "BatchSize": 25,       // chunk per chiamata a batchEmbedContents
  "TopK": 5,             // risultati di default della ricerca
  "HybridSearch": true   // false se non e' stata eseguita sql/rag_fulltext.sql
},
"Gemini": {
  "ApiKey": "",                      // chiave di Google AI Studio
  "Model": "gemini-embedding-001",
  "UseEmbedContentConfig": false,    // vedi nota sotto: lasciare false
  "RequestsPerMinute": 100,          // 100 sul piano gratuito; alzarlo se si attiva la fatturazione
  "MaxRateLimitRetries": 3,          // quanti 429 accettare aspettando il tempo indicato da Google
  "MaxRetryWaitSeconds": 120         // oltre questa attesa si fallisce invece di restare appesi
}
```

**La chiave non va committata.** In sviluppo si mette in
`appsettings.Development.json` o nei user-secrets; in produzione (Render) si
imposta la variabile d'ambiente `Gemini__ApiKey`, che sovrascrive il file.

### Dettagli che contano

- **Normalizzazione NFKC del testo estratto**: i PDF portano dentro le legature
  tipografiche del font come singoli caratteri Unicode, quindi "verifica" arriva
  scritto `veriﬁca` con U+FB01 al posto di f+i. Per il tokenizer e per il modello
  sono parole diverse, e una ricerca scritta normalmente non le trova piu'. In un
  manuale tecnico italiano riguarda buona parte delle parole che portano
  significato: file, verifica, flusso, efficace, sufficiente, anagrafica. NFKC le
  scioglie, e la stessa normalizzazione si applica alla domanda prima di
  vettorizzarla.
- **Chunking strutturale**: il taglio segue i titoli numerati e i paragrafi, mai la
  meta' di una parola, e in testa a ogni chunk finisce il percorso della sezione
  (`Manuale Utente > 5. Carica Documenti`). Un chunk sa cosi' di cosa parla anche
  quando il paragrafo, da solo, non lo direbbe. Un cambio di sezione chiude il chunk
  solo se questo e' gia' pieno al 60%, altrimenti un documento con molti titoli brevi
  produrrebbe una miriade di frammenti troppo poveri per essere trovati.
- **Ricerca ibrida**: il ramo vettoriale trova cio' che somiglia per significato, il
  ramo lessicale (`to_tsvector('italian', ...)`) trova le parole esatte. I due
  ordinamenti si fondono con Reciprocal Rank Fusion (k = 60), che non richiede di
  normalizzare punteggi di natura diversa. E' il rimedio per le ricerche su un
  termine preciso, dove il solo vettore puo' mancare il bersaglio.

- **Forma della richiesta**: `taskType` e `outputDimensionality` vanno mandati al
  **primo livello** della richiesta, non dentro `embedContentConfig`. La documentazione
  li segna come deprecati, ma sono gli unici che `batchEmbedContents` onora davvero:
  dentro `embedContentConfig` vengono ignorati in silenzio e Gemini restituisce i
  vettori nativi a 3072 dimensioni ignorando anche il taskType, senza alcun errore.
  `Gemini:UseEmbedContentConfig` va quindi lasciato a `false`; si mettera' a `true`
  quando Google rimuovera' i campi deprecati.
- **taskType asimmetrico**: i chunk sono vettorizzati con `RETRIEVAL_DOCUMENT`,
  la domanda con `RETRIEVAL_QUERY`. E' il modo in cui Gemini e' addestrato per il
  retrieval e cambia sensibilmente la qualita' dei risultati.
- **Normalizzazione L2 nel client**: `gemini-embedding-001` restituisce vettori a
  3072 dimensioni troncati via Matryoshka quando se ne chiedono 768, e i vettori
  troncati non hanno norma unitaria. La normalizzazione la fa `GeminiEmbeddingClient`.
- **Limite di input 2048 token** per `gemini-embedding-001`: i chunk da 512 token
  ci stanno con ampio margine. Alzando `Rag:ChunkSize` oltre ~2000 va cambiato modello.
- **Quota di Google e throttling**: in `batchEmbedContents` Google conta **ogni testo**
  come una richiesta, non una per chiamata. Sul piano gratuito il limite e' di 100
  richieste al minuto, quindi un singolo batch da 100 chunk esaurisce la quota e il
  batch successivo prende 429. `GeminiEmbeddingClient` ha percio' un limitatore
  condiviso a finestra scorrevole di 60 secondi che conta i testi, e sul 429 legge dal
  corpo della risposta il `retryDelay` indicato da Google e aspetta esattamente quello.
  Ordine di grandezza sul piano gratuito: circa 100 chunk al minuto, quindi un PDF da
  300 chunk richiede circa 3 minuti. Se il messaggio parla di quota giornaliera
  l'errore e' immediato: aspettare non servirebbe.
- **429 e Polly**: l'HttpClient `GeminiService` ritenta gli errori di rete e i 5xx, ma
  **non** i 429 — un backoff esponenziale di pochi secondi ricadrebbe nella stessa
  finestra ancora bloccata.

### Uso dal front-end

`Emma.Services/Services/RagServiceClient.cs` (`Emma.Services.Services`) espone gli
endpoint al client, sopra `ServiceClientBase` come tutti gli altri.

```csharp
using Emma.Services.Services;

var rag = new RagServiceClient(serverUrl, utente, password, tenant);

// 1. Indicizzazione di un PDF scelto da disco
var esito = await rag.IndicizzaPdfAsync(@"C:\documenti\contratto.pdf");
// esito.Chunks, esito.Pages, esito.Model, esito.AlreadyIndexed, esito.DurationMs

// 2. Ricerca semantica
var risultati = await rag.CercaAsync("qual e' la penale per il ritardo?", topK: 5);
foreach (var r in risultati)
    Console.WriteLine($"{r.FileName} pag.{r.Page} ({r.Score:0.00}): {r.Content}");

// 3. Contesto gia' pronto per il prompt di un LLM
string contesto = await rag.CercaContestoAsync("penale per il ritardo", topK: 4, minScore: 0.6);

// 4. Gestione dell'archivio
var documenti = await rag.GetDocumentiAsync();
await rag.EliminaDocumentoAsync(documenti[0].id);
```

Il tenant non viaggia nel body: il server lo legge dalle claim dell'utente
autenticato in Basic.

**Timeout.** L'indicizzazione e' sincrona: su un PDF lungo la chiamata puo' superare
i 100 secondi di timeout di default di `HttpClient`. In quel caso va passato al
costruttore un client con un timeout piu' generoso:

```csharp
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var rag = new RagServiceClient(http, serverUrl, utente, password, tenant);
```

La pipeline di retry dell'upload, apposta, **non** ritenta sui timeout: il server
potrebbe ancora star lavorando, e un secondo invio farebbe ripartire da zero
un'indicizzazione gia' in corso.

### Diagnosticare una ricerca che non trova

```bash
curl -u utente:password "https://.../api/v1/document/1/chunks?take=5"
```

Cosa guardare nella risposta:

- `dims` deve valere 768 e `norm` circa 1.0. Se `norm` e' molto diversa da 1 la
  normalizzazione non ha funzionato; se `dims` e' 3072 vedi la nota su
  `Gemini:UseEmbedContentConfig`.
- `content` deve essere testo italiano leggibile. Se e' scomposto, con parole
  attaccate o colonne mescolate, il problema e' l'estrazione del PDF e nessun
  embedding potra' rimediare.
- `content` deve cominciare con il percorso della sezione
  (`Manuale Utente > 5. Carica Documenti`) e non a meta' parola. Un chunk che inizia
  con un frammento tipo "adri, ciascuno dei quali..." e' stato indicizzato con il
  vecchio chunking a finestra fissa e va reindicizzato.

**Dopo un cambio di estrazione o di chunking i documenti gia' indicizzati vanno
rifatti**: il testo dei chunk cambia, quindi cambiano i vettori. Si cancella il
documento dall'archivio (`DELETE /api/v1/document/{id}` o il pulsante nella pagina
Carica Documento) e lo si ricarica; il controllo sull'hash impedirebbe altrimenti la
reindicizzazione.

### Limiti noti

- I PDF scansionati senza layer di testo producono zero chunk: l'endpoint
  risponde con un errore che invita a passare dall'OCR.
- L'indicizzazione e' sincrona: su PDF molto lunghi la chiamata resta aperta
  per tutta la durata degli embedding. Con il throttling del piano gratuito il tempo
  cresce di circa un minuto ogni 100 chunk: oltre il migliaio di chunk conviene
  passare alla fatturazione su Google oppure spostare l'indicizzazione in un
  BackgroundService con polling dello stato.
