using Microsoft.Extensions.Configuration;
using Npgsql;

namespace EmmaServer.DataSeed.Tests.Infrastructure;

/// <summary>
/// Configurazione del generatore di documenti.
///
/// Catena di configurazione, dalla priorita' piu' bassa alla piu' alta:
/// <list type="number">
///   <item>EmmaServer/appsettings.Development.json (trovato risalendo dalla cartella di output)</item>
///   <item>appsettings.DataSeed.json di questo progetto</item>
///   <item>variabili d'ambiente col prefisso <c>EMMA_</c> (separatore di sezione <c>__</c>)</item>
/// </list>
/// </summary>
public static class SeedSettings
{
    private static readonly Lazy<IConfiguration> _configurazione = new(CostruisciConfigurazione);

    public static IConfiguration Configurazione => _configurazione.Value;

    /// <summary>Tenant su cui vengono creati i documenti generati.</summary>
    public static string Tenant =>
        Configurazione["Seed:Tenant"] is { Length: > 0 } tenant ? tenant : "Test";

    /// <summary>
    /// Suffisso del numero documento, da <c>Seed:Suffisso</c> (env <c>EMMA_Seed__Suffisso</c>).
    /// Serve quando i test si lanciano UNO ALLA VOLTA: fissandolo, ordine, bolla e fattura di
    /// esecuzioni diverse restano legati dallo stesso numero. Se non e' valorizzato, ogni
    /// esecuzione ne genera uno nuovo.
    /// </summary>
    public static string? Suffisso =>
        Configurazione["Seed:Suffisso"] is { Length: > 0 } suffisso ? suffisso : null;

    public static bool DatabaseConfigurato =>
        !string.IsNullOrWhiteSpace(Configurazione["Database:Host"]) &&
        !string.IsNullOrWhiteSpace(Configurazione["Database:Database"]);

    public static string StringaDiConnessione()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Configurazione["Database:Host"],
            Database = Configurazione["Database:Database"],
            Username = Configurazione["Database:UserName"],
            Password = Configurazione["Database:Password"],
            SslMode = LeggiSslMode(),
            Timeout = 15,
            CommandTimeout = 30,
            IncludeErrorDetail = true
        };

        if (int.TryParse(Configurazione["Database:Port"], out var porta) && porta > 0)
        {
            builder.Port = porta;
        }

        return builder.ConnectionString;
    }

    private static SslMode LeggiSslMode()
    {
        var valore = Configurazione["Database:SslMode"];
        return Enum.TryParse<SslMode>(valore, ignoreCase: true, out var modalita)
            ? modalita
            : SslMode.Allow;
    }

    private static IConfiguration CostruisciConfigurazione()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory);

        var appsettingsServer = CercaAppSettingsDelServer();
        if (appsettingsServer is not null)
        {
            builder.AddJsonFile(appsettingsServer, optional: true, reloadOnChange: false);
        }

        builder.AddJsonFile("appsettings.DataSeed.json", optional: true, reloadOnChange: false);
        builder.AddEnvironmentVariables("EMMA_");

        return builder.Build();
    }

    private static string? CercaAppSettingsDelServer()
    {
        var cartella = new DirectoryInfo(AppContext.BaseDirectory);

        while (cartella is not null)
        {
            var candidato = Path.Combine(cartella.FullName, "EmmaServer", "appsettings.Development.json");
            if (File.Exists(candidato)) return candidato;

            cartella = cartella.Parent;
        }

        return null;
    }
}
