using EmmaServer.Helpers;
using Microsoft.ML.Tokenizers;
using System.Text;
using System.Text.RegularExpressions;

namespace EmmaServer.Services;

/// <summary>Un chunk di testo pronto per l'embedding.</summary>
/// <param name="Indice">Posizione del chunk nel documento, 0-based.</param>
/// <param name="Pagina">Pagina del PDF in cui inizia il chunk.</param>
/// <param name="TokenCount">Numero di token effettivi del chunk, prefisso compreso.</param>
/// <param name="Testo">Testo del chunk, con il percorso della sezione in testa.</param>
public record TestoChunk(int Indice, int Pagina, int TokenCount, string Testo);

public interface ITokenChunker
{
    /// <summary>
    /// Spezza il documento in chunk da al massimo <paramref name="chunkSize"/> token,
    /// con una sovrapposizione di circa <paramref name="overlap"/> token fatta di frasi intere.
    /// </summary>
    /// <param name="titoloDocumento">Titolo da mettere in testa a ogni chunk, insieme alla sezione.</param>
    List<TestoChunk> Chunk(IReadOnlyList<PdfPageText> pagine, int chunkSize, int overlap, string? titoloDocumento = null);

    /// <summary>Numero di token di un testo, con lo stesso tokenizer usato per il chunking.</summary>
    int ContaToken(string testo);
}

/// <summary>
/// Chunking strutturale.
///
/// La versione precedente affettava il flusso di token a finestra fissa. Funzionava, ma
/// produceva chunk che cominciavano a meta' parola ("adri, ciascuno dei quali...") e che
/// attraversavano piu' sezioni, cosi' che il vettore era la media di argomenti diversi e
/// non somigliava bene a nessuna domanda precisa.
///
/// Qui il taglio segue la struttura del documento:
///  - i titoli numerati ("5. Carica Documenti", "3.1 Barra superiore") aprono una sezione;
///  - i paragrafi restano interi, e si spezzano in frasi solo quando da soli sfondano il budget;
///  - in testa a ogni chunk si mette il percorso della sezione, cosi' il vettore sa di cosa
///    sta parlando anche quando il paragrafo, da solo, non lo direbbe;
///  - la sovrapposizione e' fatta di frasi intere, mai di frammenti.
///
/// Registrato come singleton: creare il tokenizer significa caricare il vocabolario,
/// mentre encode/decode sono operazioni thread-safe.
/// </summary>
public class TokenChunker : ITokenChunker
{
    /// <summary>Token tenuti da parte per il prefisso con titolo e sezione.</summary>
    private const int RISERVA_PREFISSO = 64;

    /// <summary>
    /// Un cambio di sezione chiude il chunk solo se questo e' gia' pieno almeno cosi'.
    /// Senza questa soglia un manuale con molti titoli brevi produrrebbe una miriade
    /// di chunk minuscoli, ognuno troppo povero per essere trovato.
    /// </summary>
    private const double RIEMPIMENTO_MINIMO = 0.6;

    // "5. Carica Documenti", "3.1 Barra superiore", "12) Uscita dal sistema".
    // Solo titoli numerati: sono inequivocabili e questo evita di scambiare per titolo
    // una riga di tabella o una voce di elenco.
    private static readonly Regex RegexTitolo =
        new(@"^(\d+(?:\.\d+)*)[.)]?\s+(\S.*)$", RegexOptions.Compiled);

    // Fine frase: punto, punto e virgola, due punti, esclamativo, interrogativo.
    private static readonly Regex RegexFineFrase =
        new(@"(?<=[.!?;:])\s+", RegexOptions.Compiled);

    private readonly Tokenizer _tokenizer;

    public TokenChunker()
    {
        // Il vocabolario arriva dal pacchetto Microsoft.ML.Tokenizers.Data.Cl100kBase,
        // e' incluso nell'assembly: nessun download a runtime.
        _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
    }

    public int ContaToken(string testo)
        => string.IsNullOrEmpty(testo) ? 0 : _tokenizer.CountTokens(testo);

    // ==================================================================
    //  Chunking
    // ==================================================================

    public List<TestoChunk> Chunk(IReadOnlyList<PdfPageText> pagine, int chunkSize, int overlap, string? titoloDocumento = null)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "La dimensione del chunk deve essere positiva.");
        if (overlap < 0 || overlap >= chunkSize)
            throw new ArgumentOutOfRangeException(nameof(overlap), "La sovrapposizione deve stare tra 0 e chunkSize - 1.");

        var chunks = new List<TestoChunk>();
        if (pagine is null || pagine.Count == 0) return chunks;

        // Budget per il contenuto vero: il prefisso occupa la riserva.
        int budget = Math.Max(chunkSize - RISERVA_PREFISSO, chunkSize / 2);

        var unita = SpezzaInUnita(EstraiBlocchi(pagine), budget);
        if (unita.Count == 0) return chunks;

        var corrente = new List<Unita>();
        int tokenCorrenti = 0;
        int indice = 0;

        void Emetti()
        {
            if (corrente.Count == 0) return;

            var prima = corrente[0];
            string prefisso = Prefisso(titoloDocumento, prima.Sezione);
            string corpo = string.Join("\n\n", corrente.Select(u => u.Testo));
            string testo = prefisso.Length > 0 ? $"{prefisso}\n\n{corpo}" : corpo;

            chunks.Add(new TestoChunk(
                Indice: indice++,
                Pagina: prima.Pagina,
                TokenCount: ContaToken(testo),
                Testo: testo));

            // Sovrapposizione: si tengono le ultime unita' intere che stanno nel budget
            // di overlap. Mai un frammento: o la frase c'e' tutta, o non c'e'.
            var coda = new List<Unita>();
            int tokenCoda = 0;

            for (int i = corrente.Count - 1; i >= 0; i--)
            {
                int tk = ContaToken(corrente[i].Testo);
                if (tokenCoda + tk > overlap) break;

                coda.Insert(0, corrente[i]);
                tokenCoda += tk;
            }

            corrente = coda;
            tokenCorrenti = tokenCoda;
        }

        foreach (var u in unita)
        {
            int tk = ContaToken(u.Testo);

            bool nonCiSta = tokenCorrenti + tk > budget;
            bool cambioSezione = corrente.Count > 0 && u.Sezione != corrente[0].Sezione;

            if (corrente.Count > 0 &&
                (nonCiSta || (cambioSezione && tokenCorrenti >= budget * RIEMPIMENTO_MINIMO)))
            {
                Emetti();

                // Dopo l'emissione la coda di sovrapposizione appartiene alla sezione
                // precedente: se cambiamo sezione si riparte puliti.
                if (corrente.Count > 0 && corrente[0].Sezione != u.Sezione)
                {
                    corrente.Clear();
                    tokenCorrenti = 0;
                }
            }

            corrente.Add(u);
            tokenCorrenti += tk;
        }

        Emetti();

        return chunks.Where(c => !string.IsNullOrWhiteSpace(c.Testo)).ToList();
    }

    // ==================================================================
    //  Struttura del documento
    // ==================================================================

    /// <summary>Un paragrafo o un titolo, con la sezione a cui appartiene e la pagina.</summary>
    private sealed record Unita(string Testo, int Pagina, string Sezione);

    /// <summary>
    /// Percorre le pagine riga per riga, riconosce i titoli numerati e accumula i paragrafi
    /// (blocchi separati da riga vuota) sotto la sezione corrente.
    /// </summary>
    private static List<Unita> EstraiBlocchi(IReadOnlyList<PdfPageText> pagine)
    {
        var blocchi = new List<Unita>();
        var gerarchia = new List<string>();   // un titolo per livello di annidamento

        var buffer = new StringBuilder();
        int paginaBuffer = 0;
        string sezioneBuffer = string.Empty;

        void ChiudiParagrafo()
        {
            if (buffer.Length == 0) return;

            var testo = buffer.ToString().Trim();
            if (testo.Length > 0)
                blocchi.Add(new Unita(testo, paginaBuffer, sezioneBuffer));

            buffer.Clear();
        }

        foreach (var pagina in pagine)
        {
            foreach (var rigaGrezza in pagina.Testo.Split('\n'))
            {
                var riga = rigaGrezza.Trim();

                if (riga.Length == 0)
                {
                    ChiudiParagrafo();
                    continue;
                }

                int livello = LivelloTitolo(riga);

                if (livello > 0)
                {
                    ChiudiParagrafo();

                    // Il titolo sostituisce quelli di pari livello e piu' profondi
                    while (gerarchia.Count >= livello) gerarchia.RemoveAt(gerarchia.Count - 1);
                    while (gerarchia.Count < livello - 1) gerarchia.Add(string.Empty);
                    gerarchia.Add(riga);

                    sezioneBuffer = string.Join(" > ", gerarchia.Where(t => t.Length > 0));

                    // Il titolo entra anche come unita': da solo e' una risposta possibile
                    blocchi.Add(new Unita(riga, pagina.Numero, sezioneBuffer));
                    continue;
                }

                if (buffer.Length == 0)
                {
                    paginaBuffer = pagina.Numero;
                }
                else
                {
                    buffer.Append(' ');
                }

                buffer.Append(riga);
            }
        }

        ChiudiParagrafo();

        return blocchi;
    }

    /// <summary>Livello del titolo: 1 per "5.", 2 per "3.1", 3 per "3.1.2". 0 se non e' un titolo.</summary>
    private static int LivelloTitolo(string riga)
    {
        // Un titolo e' corto: una riga lunga che comincia per numero e' una voce di elenco
        if (riga.Length > 90) return 0;

        var m = RegexTitolo.Match(riga);
        if (!m.Success) return 0;

        // Deve esserci del testo dopo il numero, non altri numeri
        var titolo = m.Groups[2].Value.Trim();
        if (titolo.Length < 2 || !titolo.Any(char.IsLetter)) return 0;

        return m.Groups[1].Value.Split('.').Length;
    }

    // ==================================================================
    //  Adattamento al budget
    // ==================================================================

    /// <summary>
    /// Garantisce che nessuna unita' superi da sola il budget: i paragrafi troppo lunghi
    /// si spezzano in frasi, e una frase mostruosa si spezza sulle parole. Mai a meta' parola.
    /// </summary>
    private List<Unita> SpezzaInUnita(List<Unita> blocchi, int budget)
    {
        var risultato = new List<Unita>();

        foreach (var blocco in blocchi)
        {
            if (ContaToken(blocco.Testo) <= budget)
            {
                risultato.Add(blocco);
                continue;
            }

            foreach (var pezzo in SpezzaTesto(blocco.Testo, budget))
                risultato.Add(blocco with { Testo = pezzo });
        }

        return risultato;
    }

    private IEnumerable<string> SpezzaTesto(string testo, int budget)
    {
        var accumulato = new StringBuilder();
        int tokenAccumulati = 0;

        foreach (var frase in RegexFineFrase.Split(testo))
        {
            if (string.IsNullOrWhiteSpace(frase)) continue;

            foreach (var pezzo in SpezzaFrase(frase, budget))
            {
                int tk = ContaToken(pezzo);

                if (tokenAccumulati > 0 && tokenAccumulati + tk > budget)
                {
                    yield return accumulato.ToString().Trim();
                    accumulato.Clear();
                    tokenAccumulati = 0;
                }

                if (accumulato.Length > 0) accumulato.Append(' ');
                accumulato.Append(pezzo);
                tokenAccumulati += tk;
            }
        }

        if (accumulato.Length > 0) yield return accumulato.ToString().Trim();
    }

    /// <summary>Ultima risorsa: una frase piu' lunga del budget si taglia sui confini di parola.</summary>
    private IEnumerable<string> SpezzaFrase(string frase, int budget)
    {
        if (ContaToken(frase) <= budget)
        {
            yield return frase;
            yield break;
        }

        var accumulato = new StringBuilder();
        int tokenAccumulati = 0;

        foreach (var parola in frase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int tk = ContaToken(parola + " ");

            if (tokenAccumulati > 0 && tokenAccumulati + tk > budget)
            {
                yield return accumulato.ToString().Trim();
                accumulato.Clear();
                tokenAccumulati = 0;
            }

            if (accumulato.Length > 0) accumulato.Append(' ');
            accumulato.Append(parola);
            tokenAccumulati += tk;
        }

        if (accumulato.Length > 0) yield return accumulato.ToString().Trim();
    }

    /// <summary>Riga di contesto in testa al chunk: "Manuale EMMA > 5. Carica Documenti".</summary>
    private static string Prefisso(string? titoloDocumento, string sezione)
    {
        var parti = new List<string>();

        if (!string.IsNullOrWhiteSpace(titoloDocumento)) parti.Add(titoloDocumento.Trim());
        if (!string.IsNullOrWhiteSpace(sezione)) parti.Add(sezione.Trim());

        return parti.Count == 0 ? string.Empty : string.Join(" > ", parti);
    }
}
