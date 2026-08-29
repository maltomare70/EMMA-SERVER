using Npgsql;

namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Verifica una sola volta per esecuzione se il database dei test e' raggiungibile.
/// Se non lo e' i test di integrazione vengono saltati con un messaggio chiaro invece di fallire
/// in massa con un timeout di connessione.
/// </summary>
public static class TestDatabase
{
    private static readonly Lazy<string?> _motivoSkip = new(VerificaConnessione, isThreadSafe: true);

    /// <summary>null se il database e' raggiungibile, altrimenti il motivo dello skip.</summary>
    public static string? MotivoSkip => _motivoSkip.Value;

    public static bool Disponibile => MotivoSkip is null;

    private static string? VerificaConnessione()
    {
        if (!TestSettings.DatabaseConfigurato)
        {
            return "Database dei test non configurato: valorizza Database:Host / Database:Database in " +
                   "appsettings.Tests.json oppure esporta EMMA_Database__Host e EMMA_Database__Database.";
        }

        try
        {
            using var connessione = new NpgsqlConnection(TestSettings.StringaDiConnessione());
            connessione.Open();

            using var comando = new NpgsqlCommand("SELECT 1", connessione);
            comando.ExecuteScalar();

            return null;
        }
        catch (Exception eccezione)
        {
            return $"Database dei test non raggiungibile ({TestSettings.Configurazione["Database:Host"]}): {eccezione.Message}";
        }
    }
}
