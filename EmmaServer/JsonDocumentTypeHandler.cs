using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace EmmaServer;

/// <summary>
/// Type handler Dapper per le colonne PostgreSQL <c>jsonb</c> mappate su <see cref="JsonDocument"/>.
/// Va registrato una sola volta all'avvio: <c>SqlMapper.AddTypeHandler(new JsonDocumentTypeHandler());</c>
/// </summary>
public sealed class JsonDocumentTypeHandler : SqlMapper.TypeHandler<JsonDocument>
{
    /// <summary>Viene eseguito quando SALVI nel database.</summary>
    public override void SetValue(IDbDataParameter parameter, JsonDocument? value)
    {
        // Forza il driver Postgres a capire che è un JSONB: senza questo il testo
        // verrebbe inviato come "text" e il cast implicito a jsonb fallirebbe.
        if (parameter is NpgsqlParameter npgsqlParameter)
        {
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        }

        parameter.Value = value is null
            ? DBNull.Value
            : value.RootElement.GetRawText();
    }

    /// <summary>
    /// Viene eseguito quando LEGGI dal database.
    /// Il <see cref="JsonDocument"/> restituito è di proprietà del chiamante: implementa
    /// <see cref="IDisposable"/> e andrebbe rilasciato quando non serve più.
    /// </summary>
    public override JsonDocument? Parse(object value) => value switch
    {
        null or DBNull => null,
        JsonDocument document => document,
        string json => JsonDocument.Parse(json),
        byte[] utf8Json => JsonDocument.Parse(utf8Json),
        _ => JsonDocument.Parse(value.ToString()!)
    };
}
