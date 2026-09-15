using System.Text.Json.Serialization;

namespace EmmaServer.Entities.Dtos;

/// <summary>Filtri di GET /api/v1/anomalie.</summary>
public class FiltriAnomalie
{
    /// <summary>0 aperta, 1 confermata, 2 ignorata. Null = tutti.</summary>
    public short? Stato { get; set; }

    /// <summary>Severita' MINIMA. Default 1: gli avvisi in ombra (0) non si vedono se non richiesti.</summary>
    public short? Severita { get; set; }

    /// <summary>Id del fornitore (anomalie.id_fornitore).</summary>
    public int? Fornitore { get; set; }

    /// <summary>Data di creazione dell'avviso, dal giorno indicato (incluso).</summary>
    public DateTime? Da { get; set; }

    /// <summary>Data di creazione dell'avviso, fino al giorno indicato (incluso).</summary>
    public DateTime? A { get; set; }

    public short? Livello { get; set; }
    public string? Tipo { get; set; }
    public int? DocId { get; set; }

    public int Limit { get; set; } = 100;
    public int Offset { get; set; }
}

/// <summary>Un avviso della lista, con i dati del documento e del fornitore per mostrarlo senza altre chiamate.</summary>
public class AnomaliaLista : EmmaAnomalia
{
    public string? fornitore_descrizione { get; set; }
    public string? mittente { get; set; }
    public string? numero_documento { get; set; }
    public string? data_documento { get; set; }
    public string? tipo_documento { get; set; }
}

public class PaginaAnomalie
{
    [JsonPropertyName("totale")]
    public int Totale { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("righe")]
    public List<AnomaliaLista> Righe { get; set; } = [];
}

/// <summary>Corpo di POST /api/v1/anomalie/{id}/feedback.</summary>
public class FeedbackAnomalia
{
    /// <summary>1 confermata, 2 ignorata; 0 riapre l'avviso e cancella il feedback.</summary>
    [JsonPropertyName("stato")]
    public short Stato { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>Contatori e precisione per un tipo di avviso (o complessivi).</summary>
public class StatisticaAnomalie
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("livello")]
    public short? Livello { get; set; }

    [JsonPropertyName("totali")]
    public int Totali { get; set; }

    [JsonPropertyName("aperte")]
    public int Aperte { get; set; }

    [JsonPropertyName("confermate")]
    public int Confermate { get; set; }

    [JsonPropertyName("ignorate")]
    public int Ignorate { get; set; }

    /// <summary>confermate / (confermate + ignorate); null finche' non c'e' nessun feedback.</summary>
    [JsonPropertyName("precisione")]
    public double? Precisione { get; set; }

    /// <summary>
    /// Vero se la precisione e' sotto la soglia (30%) con abbastanza feedback per crederci:
    /// l'operatore smettera' di guardare questi avvisi.
    /// </summary>
    [JsonPropertyName("sotto_soglia")]
    public bool SottoSoglia { get; set; }
}

/// <summary>Risposta di GET /api/v1/anomalie/statistiche. Conta solo gli avvisi visibili (severita' > 0).</summary>
public class StatisticheAnomalie
{
    [JsonPropertyName("da")]
    public DateTime? Da { get; set; }

    [JsonPropertyName("a")]
    public DateTime? A { get; set; }

    [JsonPropertyName("soglia_precisione")]
    public double SogliaPrecisione { get; set; }

    [JsonPropertyName("complessivo")]
    public StatisticaAnomalie Complessivo { get; set; } = new();

    [JsonPropertyName("per_tipo")]
    public List<StatisticaAnomalie> PerTipo { get; set; } = [];
}
