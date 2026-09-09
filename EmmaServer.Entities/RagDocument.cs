using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

/// <summary>
/// Anagrafica di un PDF indicizzato nel database vettoriale.
/// I pezzi di testo con i relativi vettori stanno in <see cref="RagChunk"/>.
/// </summary>
[Table("rag_documents")]
public class RagDocument : IEntity
{
    /// <summary>Stato: indicizzazione in corso.</summary>
    public const int STATO_IN_CORSO = 0;
    /// <summary>Stato: documento indicizzato correttamente.</summary>
    public const int STATO_OK = 1;
    /// <summary>Stato: indicizzazione fallita.</summary>
    public const int STATO_ERRORE = -1;

    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }

    [Write(false)]
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;

    public string tenant { get; set; } = string.Empty;
    public string file_name { get; set; } = string.Empty;

    /// <summary>SHA-256 del PDF: identifica il file a prescindere dal nome.</summary>
    public string file_hash { get; set; } = string.Empty;

    public long file_size { get; set; }
    public int pagine { get; set; }
    public int chunk_count { get; set; }
    public int chunk_size { get; set; }
    public int chunk_overlap { get; set; }
    public string embedding_model { get; set; } = string.Empty;
    public int embedding_dim { get; set; }
    public int stato { get; set; } = STATO_IN_CORSO;
    public string? messaggio { get; set; }

    /// <summary>PDF originale.</summary>
    public byte[]? allegato { get; set; }
}
