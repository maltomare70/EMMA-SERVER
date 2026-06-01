using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;


namespace EmmaServer;


public class JsonDocumentTypeHandler : SqlMapper.TypeHandler<JsonDocument>
{
    // Viene eseguito quando SALVI nel database
    public override void SetValue(IDbDataParameter parameter, JsonDocument value)
    {
        if (parameter is NpgsqlParameter npgsqlParameter)
        {
            // Forza il driver Postgres a capire che è un JSONB
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            npgsqlParameter.Value = value?.RootElement.GetRawText() ?? (object)DBNull.Value;
        }
        else
        {
            parameter.Value = value?.RootElement.GetRawText() ?? (object)DBNull.Value;
        }
    }

    // Viene eseguito quando LEGGI dal database
    public override JsonDocument Parse(object value)
    {
        if (value == null || value is DBNull) return null;
        
        string json = value.ToString();
        return JsonDocument.Parse(json);
    }
}