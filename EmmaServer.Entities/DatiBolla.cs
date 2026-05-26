using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace EmmaServer.Entities;


// Questo rappresenta il JSON principale che unisce i costi e i dati estratti
public class RispostaDdt
{
    [JsonPropertyName("costs")]
    public Costs Costs { get; set; } = new();

    [JsonPropertyName("dati_bolla")]
    public DatiBolla DatiBolla { get; set; } = new();
}

public class Costs
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("total_cost_eur")]
    public double TotalCostEur { get; set; } // float in Python si mappa in double o decimal
}

public class DatiBolla
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("tipo_documento")]
    public string TipoDocumento { get; set; } = string.Empty;

    [JsonPropertyName("numero_bolla")]
    public string NumeroBolla { get; set; } = string.Empty;

    [JsonPropertyName("data_bolla")]
    public string DataBolla { get; set; } = string.Empty;

    [JsonPropertyName("mittente")]
    public string Mittente { get; set; } = string.Empty;

    [JsonPropertyName("articoli")]
    public List<ArticoloBolla> Articoli { get; set; } = new();

    [JsonPropertyName("imponibile")]
    public double Imponibile { get; set; }

    [JsonPropertyName("sconto")]
    public string Sconto { get; set; } = string.Empty;

    [JsonPropertyName("iva")]
    public string Iva { get; set; } = string.Empty;

    [JsonPropertyName("totale")]
    public double Totale { get; set; }
}

public class ArticoloBolla
{
    [JsonPropertyName("codice")]
    public string Codice { get; set; } = string.Empty;

    [JsonPropertyName("descrizione")]
    public string Descrizione { get; set; } = string.Empty;

    [JsonPropertyName("quantita")]
    public double Quantita { get; set; }

    [JsonPropertyName("unita_misura")]
    public string UnitaMisura { get; set; } = string.Empty;

    [JsonPropertyName("imponibile")]
    public double Imponibile { get; set; }

    [JsonPropertyName("iva")]
    public string Iva { get; set; } = string.Empty;

    [JsonPropertyName("totale")]
    public double Totale { get; set; }
}