namespace EmmaServer.Entities;

/// <summary>
/// Riga della tabella <c>anomalie</c> (vedi Migrations/sql_003.sql).
/// Non usa Dapper.Contrib: la chiave e' bigserial e le scritture passano da SQL esplicito
/// in AnomalieRepository.
/// </summary>
public class EmmaAnomalia
{
    public long id { get; set; }
    public string tenant { get; set; } = string.Empty;
    public int doc_id { get; set; }
    public string? id_master { get; set; }
    public string? id_riga { get; set; }
    public int? id_fornitore { get; set; }
    public string? codice { get; set; }
    public string tipo { get; set; } = string.Empty;
    public short livello { get; set; }
    public double? score { get; set; }
    public short severita { get; set; }
    public decimal? valore { get; set; }
    public decimal? atteso { get; set; }
    public decimal? delta_perc { get; set; }
    public string? messaggio { get; set; }
    public string? modello_ver { get; set; }

    /// <summary>0 aperta, 1 confermata, 2 ignorata.</summary>
    public short stato { get; set; }
    public string? utente_feedback { get; set; }
    public DateTime? data_feedback { get; set; }

    /// <summary>Nota libera dell'operatore insieme al feedback.</summary>
    public string? note { get; set; }
    public DateTime data_creazione { get; set; }
}

/// <summary>Valori ammessi per <see cref="EmmaAnomalia.tipo"/>.</summary>
public static class TipoAnomalia
{
    // Livello 1 - regole sul singolo documento
    public const string RigaIncoerente = "riga_incoerente";
    public const string ValoreImpossibile = "valore_impossibile";
    public const string AliquotaIva = "aliquota_iva";
    public const string DataAnomala = "data_anomala";
    public const string RigheGemelle = "righe_gemelle";

    /// <summary>Tipi di livello 1 che rendono inaffidabile l'intero documento (fuori da baseline e livello 2).</summary>
    public static readonly IReadOnlySet<string> BloccantiDocumento = new HashSet<string> { "incoerenza_totali", "duplicato" };

    /// <summary>Tipi di livello 1 che rendono inaffidabile il prezzo di una riga.</summary>
    public static readonly IReadOnlySet<string> BloccantiRiga = new HashSet<string> { "riga_incoerente", "valore_impossibile" };

    public const string PrezzoAlto = "prezzo_alto";
    public const string PrezzoBasso = "prezzo_basso";
    public const string QtaAnomala = "qta_anomala";
    public const string UmCambiata = "um_cambiata";
    public const string IncoerenzaTotali = "incoerenza_totali";
    public const string Duplicato = "duplicato";
    public const string RigaAnomala = "riga_anomala";
    public const string NuovoListino = "nuovo_listino";
}

/// <summary>
/// Riga della tabella <c>anomalie_baseline</c>.
/// Attenzione alla scala: <see cref="mediana_qta"/> e <see cref="mad_qta"/> sono calcolate su ln(quantita).
/// </summary>
public class EmmaAnomaliaBaseline
{
    public long id { get; set; }
    public string tenant { get; set; } = string.Empty;
    public int id_fornitore { get; set; }
    public string codice { get; set; } = string.Empty;
    public short tipo_doc { get; set; }
    public int n { get; set; }
    public decimal? mediana_prezzo { get; set; }
    public decimal? mad_prezzo { get; set; }
    public decimal? mediana_qta { get; set; }
    public decimal? mad_qta { get; set; }
    public string? um_prevalente { get; set; }
    public DateTime? primo_visto { get; set; }
    public DateTime? ultimo_visto { get; set; }
    public DateTime aggiornata_il { get; set; }
}
