namespace EmmaServer.Entities;

/// <summary>
/// Un pezzo di testo del PDF con il suo vettore.
/// Non passa da Dapper.Contrib: l'inserimento e' massivo e l'embedding
/// viaggia come letterale testuale con cast esplicito a vector.
/// </summary>
public class RagChunk
{
    public int id { get; set; }
    public int id_doc { get; set; }
    public string tenant { get; set; } = string.Empty;

    /// <summary>Posizione del chunk nel documento (0-based).</summary>
    public int chunk_index { get; set; }

    /// <summary>Pagina del PDF in cui inizia il chunk (1-based, 0 se sconosciuta).</summary>
    public int pagina { get; set; }

    public int token_count { get; set; }
    public string contenuto { get; set; } = string.Empty;

    /// <summary>Vettore dell'embedding. Non viene riletto dalle query di ricerca.</summary>
    public float[] embedding { get; set; } = Array.Empty<float>();

    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
}
