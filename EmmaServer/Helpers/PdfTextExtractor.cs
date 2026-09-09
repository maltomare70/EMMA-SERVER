using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EmmaServer.Helpers;

/// <summary>Testo di una singola pagina del PDF.</summary>
/// <param name="Numero">Numero di pagina, 1-based.</param>
/// <param name="Testo">Testo della pagina gia' normalizzato.</param>
public record PdfPageText(int Numero, string Testo);

/// <summary>
/// Estrazione del testo da un PDF con PdfPig (Apache 2.0, 100% managed).
/// Attenzione: legge solo il layer di testo, un PDF scansionato senza OCR
/// restituisce pagine vuote.
/// </summary>
public static class PdfTextExtractor
{
    private static readonly Regex SpaziMultipli = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private static readonly Regex RigheVuote = new(@"(\r?\n){3,}", RegexOptions.Compiled);

    /// <summary>
    /// Restituisce il testo pagina per pagina. Le pagine senza testo vengono scartate.
    /// </summary>
    /// <param name="pdf">Contenuto del file PDF.</param>
    /// <param name="numeroPagine">Numero totale di pagine del PDF, comprese quelle senza testo.</param>
    public static List<PdfPageText> EstraiPagine(byte[] pdf, out int numeroPagine)
    {
        var pagine = new List<PdfPageText>();

        using var document = PdfDocument.Open(pdf);
        numeroPagine = document.NumberOfPages;

        foreach (var page in document.GetPages())
        {
            string testo;
            try
            {
                // Ricostruisce l'ordine di lettura (colonne, blocchi) meglio di page.Text
                testo = ContentOrderTextExtractor.GetText(page, true);
            }
            catch
            {
                testo = page.Text;
            }

            testo = Normalizza(testo);

            if (!string.IsNullOrWhiteSpace(testo))
                pagine.Add(new PdfPageText(page.Number, testo));
        }

        return pagine;
    }

    private static string Normalizza(string? testo)
    {
        if (string.IsNullOrWhiteSpace(testo)) return string.Empty;

        // PASSAGGIO CRITICO PER LA RICERCA.
        // I PDF portano dentro le legature tipografiche del font come singoli caratteri
        // Unicode: "verifica" arriva scritto "veri\uFB01ca", con U+FB01 al posto di f+i.
        // Sono parole diverse per il tokenizer e per il modello di embedding, e una
        // ricerca scritta normalmente non le trova piu'. In un manuale tecnico italiano
        // riguarda buona parte delle parole che portano significato (file, verifica,
        // flusso, efficace, sufficiente, anagrafica...).
        // NFKC scioglie \uFB00-\uFB04 in ff/fi/fl/ffi/ffl e uniforma le altre forme
        // di compatibilita'.
        try
        {
            testo = testo.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Sequenze Unicode non valide: si tiene il testo com'e' piuttosto che perderlo.
        }

        // Sillabazione a fine riga: "docu-\nmento" -> "documento"
        testo = testo.Replace("-\r\n", string.Empty).Replace("-\n", string.Empty);
        testo = testo.Replace("\r\n", "\n").Replace('\r', '\n');
        testo = SpaziMultipli.Replace(testo, " ");
        testo = RigheVuote.Replace(testo, "\n\n");

        return testo.Trim();
    }
}
