using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EmmaServer.Entities.Dtos;


// ==========================================
// 1. Modelli per Input
// ==========================================

public class DettaglioDocumento
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("codice")]
    public string Codice { get; set; } = string.Empty;

    [JsonPropertyName("qta")]
    public decimal Qta { get; set; }
}

/// <summary>Classe contenitore per l'input unico</summary>
public class InputConciliazione
{
    [JsonPropertyName("fornitore")]
    public string Fornitore { get; set; } = string.Empty;

    [JsonPropertyName("bolle")]
    public List<DettaglioDocumento> Bolle { get; set; } = new();

    [JsonPropertyName("fatture")]
    public List<DettaglioDocumento> Fatture { get; set; } = new();
}

// ==========================================
// 2. Modelli per Output
// ==========================================

public class BollaCollegata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("codice_originale")]
    public string CodiceOriginale { get; set; } = string.Empty;

    [JsonPropertyName("qta")]
    public decimal Qta { get; set; }

    [JsonPropertyName("fuzzy_score")]
    public double FuzzyScore { get; set; }
}

public class DettaglioFattura
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("codice")]
    public string Codice { get; set; } = string.Empty;

    [JsonPropertyName("qta")]
    public decimal Qta { get; set; }
}

public class Conciliazione
{
    [JsonPropertyName("fattura")]
    public DettaglioFattura Fattura { get; set; } = new();

    [JsonPropertyName("bolle_collegate")]
    public List<BollaCollegata> BolleCollegate { get; set; } = new();

    [JsonPropertyName("totale_qta_bolle")]
    public decimal TotaleQtaBolle { get; set; }

    [JsonPropertyName("stato_conciliazione")]
    public string StatoConciliazione { get; set; } = string.Empty;

    [JsonPropertyName("differenza_qta")]
    public decimal DifferenzaQta { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}

public class ResponseConciliazione
{
    [JsonPropertyName("struttura")]
    public string Struttura { get; set; } = "conciliazione_bolle_fatture";

    [JsonPropertyName("fornitore")]
    public string Fornitore { get; set; } = string.Empty;

    [JsonPropertyName("conciliazioni")]
    public List<Conciliazione> Conciliazioni { get; set; } = new();

    [JsonPropertyName("bolle_non_conciliate")]
    public List<DettaglioDocumento> BolleNonConciliate { get; set; } = new();

    [JsonPropertyName("fatture_non_conciliate")]
    public List<DettaglioDocumento> FattureNonConciliate { get; set; } = new();
}

public class ConciliazioneResponse
{
    /// <summary>Costs</summary>
    [JsonPropertyName("costs")]
    public Costs Costs { get; set; } = new();

    /// <summary>Output Procedura di Conciliazione</summary>
    [JsonPropertyName("document")]
    public ResponseConciliazione Document { get; set; } = new();
}
