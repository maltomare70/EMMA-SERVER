using System.Text.Json.Serialization;

namespace EmmaServer.Entities.Dtos;

// =====================================================================
//  Risposta dell'indicizzazione  ->  POST /api/v1/document
// =====================================================================

public class DocumentResponse
{
    [JsonPropertyName("doc_id")]
    public int DocId { get; set; }

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }

    [JsonPropertyName("tokens")]
    public int Tokens { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    /// <summary>true se il file era gia' presente (stesso hash) e non e' stato reindicizzato.</summary>
    [JsonPropertyName("already_indexed")]
    public bool AlreadyIndexed { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }
}

// =====================================================================
//  Ricerca semantica  ->  POST /api/v1/document/search
// =====================================================================

public class RagSearchRequest
{
    /// <summary>Testo della domanda.</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>Numero di chunk da restituire.</summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 5;

    /// <summary>Se valorizzato limita la ricerca a un singolo documento.</summary>
    [JsonPropertyName("doc_id")]
    public int? DocId { get; set; }

    /// <summary>Similarita' coseno minima (0..1) per tenere un risultato.</summary>
    [JsonPropertyName("min_score")]
    public double? MinScore { get; set; }
}

public class RagSearchResult
{
    [JsonPropertyName("doc_id")]
    public int DocId { get; set; }

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Similarita' coseno: 1 = identico, 0 = ortogonale.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }
}

// =====================================================================
//  Diagnostica  ->  GET /api/v1/document/{id}/chunks
//  Serve a vedere cosa e' finito davvero nel database vettoriale:
//  il testo estratto dal PDF e la salute del vettore.
// =====================================================================

public class RagChunkInfo
{
    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("token_count")]
    public int TokenCount { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Numero di dimensioni del vettore salvato: deve valere Rag:EmbeddingDim.</summary>
    [JsonPropertyName("dims")]
    public int Dims { get; set; }

    /// <summary>Norma L2 del vettore: dopo la normalizzazione deve valere circa 1.</summary>
    [JsonPropertyName("norm")]
    public double Norm { get; set; }
}
