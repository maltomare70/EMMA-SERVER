using Microsoft.Extensions.Configuration;
using Npgsql;

namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Configurazione dei test di integrazione.
///
/// La catena di configurazione, dalla priorita' piu' bassa alla piu' alta, e':
/// <list type="number">
///   <item>EmmaServer/appsettings.Development.json (se lo si trova risalendo dalla cartella di output)</item>
///   <item>appsettings.Tests.json copiato nella cartella di output del progetto di test</item>
///   <item>variabili d'ambiente col prefisso <c>EMMA_</c> (separatore di sezione <c>__</c>)</item>
/// </list>
///
/// Cosi' di default i test puntano allo stesso database del progetto server, ma su una macchina
/// diversa (o in CI) basta esportare, per esempio:
/// <code>
/// EMMA_Database__Host=localhost
/// EMMA_Database__Database=emma
/// EMMA_Database__UserName=postgres
/// EMMA_Database__Password=postgres
/// EMMA_Database__SslMode=Disable
/// EMMA_Test__Tenant=test-locale
/// </code>
/// </summary>
public static class TestSettings
{
    private static readonly Lazy<IConfiguration> _configurazione = new(CostruisciConfigurazione);

    public static IConfiguration Configurazione => _configurazione.Value;

    /// <summary>Tenant usato da tutti i test: tiene le bolle di prova separate dai dati reali.</summary>
    public static string Tenant =>
        Configurazione["Test:Tenant"] is { Length: > 0 } tenant ? tenant : "test-xunit";

    /// <summary>true se nella configurazione c'e' abbastanza roba per provare a connettersi.</summary>
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
            : SslMode.Require;
    }

    private static IConfiguration CostruisciConfigurazione()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory);

        // 1. appsettings.Development.json del server: evita di duplicare le credenziali nel repo.
        var appsettingsServer = CercaAppSettingsDelServer();
        if (appsettingsServer is not null)
        {
            builder.AddJsonFile(appsettingsServer, optional: true, reloadOnChange: false);
        }

        // 2. impostazioni specifiche dei test.
        builder.AddJsonFile("appsettings.Tests.json", optional: true, reloadOnChange: false);

        // 3. variabili d'ambiente.
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
