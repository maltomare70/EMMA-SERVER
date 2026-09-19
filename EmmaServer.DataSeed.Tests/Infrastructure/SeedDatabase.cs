using Npgsql;

namespace EmmaServer.DataSeed.Tests.Infrastructure;

/// <summary>
/// Verifica una sola volta per esecuzione se il database e' raggiungibile: se non lo e'
/// i test di generazione vengono saltati con un messaggio chiaro invece di fallire in massa.
/// </summary>
public static class SeedDatabase
{
    private static readonly Lazy<string?> _motivoSkip = new(VerificaConnessione, isThreadSafe: true);

    /// <summary>null se il database e' raggiungibile, altrimenti il motivo dello skip.</summary>
    public static string? MotivoSkip => _motivoSkip.Value;

    public static bool Disponibile => MotivoSkip is null;

    private static string? VerificaConnessione()
    {
        if (!SeedSettings.DatabaseConfigurato)
        {
            return "Database non configurato: valorizza Database:Host / Database:Database in " +
                   "appsettings.DataSeed.json oppure esporta EMMA_Database__Host e EMMA_Database__Database.";
        }

        try
        {
            using var connessione = new NpgsqlConnection(SeedSettings.StringaDiConnessione());
            connessione.Open();

            using var comando = new NpgsqlCommand("SELECT 1", connessione);
            comando.ExecuteScalar();

            return null;
        }
        catch (Exception eccezione)
        {
            return $"Database non raggiungibile ({SeedSettings.Configurazione["Database:Host"]}): {eccezione.Message}";
        }
    }
}
