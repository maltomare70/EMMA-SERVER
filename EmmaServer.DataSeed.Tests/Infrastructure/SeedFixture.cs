using Dapper;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Repositories;
using EmmaServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EmmaServer.DataSeed.Tests.Infrastructure;

/// <summary>
/// Container di dipendenze con i servizi VERI di EmmaServer, come <c>DocServiceFixture</c> di
/// EmmaServer.Tests: l'unica sostituzione e' <see cref="IUserConnectionProvider"/>, che fuori da
/// una richiesta HTTP non saprebbe da dove prendere tenant e connessione.
///
/// I documenti generati finiscono davvero sulla tabella <c>docs</c>, sotto il tenant di
/// <see cref="SeedSettings.Tenant"/>.
/// </summary>
public sealed class SeedFixture : IDisposable
{
    private static readonly object _lucchetto = new();
    private static bool _typeHandlerRegistrato;

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public SeedFixture()
    {
        RegistraTypeHandlerDapper();

        Tenant = SeedSettings.Tenant;
        StringaDiConnessione = SeedSettings.DatabaseConfigurato
            ? SeedSettings.StringaDiConnessione()
            : string.Empty;

        var servizi = new ServiceCollection();

        servizi.AddSingleton<IConfiguration>(SeedSettings.Configurazione);
        servizi.AddLogging();

        // Nessun documento viene importato dall'AI: il client serve solo a soddisfare il costruttore.
        servizi.AddHttpClient();

        servizi.AddSingleton<IUserConnectionProvider>(
            new SeedUserConnectionProvider(StringaDiConnessione, Tenant));

        servizi.AddScoped(typeof(IRepositoryGenerico<>), typeof(RepositoryGenerico<>));
        servizi.AddScoped<IDocRepository, global::EmmaServer.Repositories.DocRepository>();
        servizi.AddScoped<IFornitoriRepository, global::EmmaServer.Repositories.FornitoriRepository>();
        servizi.AddScoped<IArticoliRepository, global::EmmaServer.Repositories.ArticoliRepository>();
        servizi.AddScoped<IConciliaRigheRepository, global::EmmaServer.Repositories.ConciliaRigheRepository>();
        servizi.AddScoped<IAnomalieRepository, global::EmmaServer.Repositories.AnomalieRepository>();
        servizi.AddScoped<IFornitoriService, global::EmmaServer.Services.FornitoriService>();
        servizi.AddScoped<IArticoliService, global::EmmaServer.Services.ArticoliService>();
        servizi.AddScoped<ILogService, global::EmmaServer.Services.LogService>();
        servizi.AddScoped<IConciliaRigheService, global::EmmaServer.Services.ConciliaRigheService>();
        servizi.AddScoped<IAnomalieService, global::EmmaServer.Services.AnomalieService>();
        servizi.AddScoped<IDocService, global::EmmaServer.Services.DocService>();

        _provider = servizi.BuildServiceProvider();
        _scope = _provider.CreateScope();

        DocService = _scope.ServiceProvider.GetRequiredService<IDocService>();
        DocRepository = _scope.ServiceProvider.GetRequiredService<IDocRepository>();
    }

    /// <summary>Tenant sotto cui vengono creati tutti i documenti generati.</summary>
    public string Tenant { get; }

    /// <summary>
    /// Suffisso del numero documento condiviso da tutti i test di questa esecuzione: lanciando la
    /// classe intera si ottiene UNA terna coerente (ORD-, DDT-, FT- con lo stesso suffisso), mentre
    /// due esecuzioni consecutive non si sovrappongono.
    ///
    /// Lanciando i test uno alla volta ogni esecuzione avrebbe un suffisso diverso: per tenere
    /// legata la terna basta fissarlo con <c>Seed:Suffisso</c> (env <c>EMMA_Seed__Suffisso</c>).
    /// </summary>
    public string SuffissoEsecuzione { get; } = SeedSettings.Suffisso ?? DocumentoFactory.SuffissoUnivoco();

    public string StringaDiConnessione { get; }

    public IDocService DocService { get; }

    public IDocRepository DocRepository { get; }

    /// <summary>
    /// Salva sul database un documento gia' costruito da <see cref="DocumentoFactory"/> e
    /// restituisce l'id della riga in <c>docs</c>.
    /// </summary>
    /// <param name="documento">Il documento da salvare.</param>
    /// <param name="aggiornaAnagrafiche">
    /// Se true chiama <c>AddOrUpdateFornitorieArticoli</c>, come fa l'import reale: popola
    /// <c>fornitori</c> sempre e <c>articoli</c> solo per i DDT (tipo 2), che e' la regola di
    /// <c>ArticoliService</c>.
    /// </param>
    public async Task<int> SalvaDocumentoAsync(DdtResponse documento, bool aggiornaAnagrafiche = true)
    {
        var salvato = await DocService.AddDocAsync(
            DocumentoFactory.FiltriPer(documento),
            DocumentoFactory.ToJson(documento),
            documento.FileName ?? string.Empty,
            DocumentoFactory.AllegatoFinto(documento),
            Tenant);

        if (salvato is null)
        {
            throw new InvalidOperationException(
                $"Documento non salvato: {documento.Document.TipoDocumento} {documento.Document.NumeroBolla}");
        }

        if (aggiornaAnagrafiche)
        {
            await DocService.AddOrUpdateFornitorieArticoli(salvato.id);
        }

        return salvato.id;
    }

    /// <summary>Conta i documenti del tenant, per un controllo a fine generazione.</summary>
    public async Task<int> ContaDocumentiDelTenantAsync()
    {
        await using var connessione = new NpgsqlConnection(StringaDiConnessione);
        return await connessione.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM docs WHERE tenant = @Tenant", new { Tenant = Tenant });
    }

    private static void RegistraTypeHandlerDapper()
    {
        lock (_lucchetto)
        {
            if (_typeHandlerRegistrato) return;

            // In produzione lo fa Program.cs: senza, le colonne jsonb non vengono mappate su JsonDocument.
            SqlMapper.AddTypeHandler(new JsonDocumentTypeHandler());
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            _typeHandlerRegistrato = true;
        }
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

/// <summary>
/// Una sola fixture condivisa: i test di generazione girano in sequenza e non si pestano i piedi.
/// </summary>
[CollectionDefinition(NomeCollezioneSeed.Nome)]
public sealed class SeedCollection : ICollectionFixture<SeedFixture>
{
    // Classe volutamente vuota: serve solo come punto di aggancio per xUnit.
}

public static class NomeCollezioneSeed
{
    public const string Nome = "Generazione documenti EMMA";
}
