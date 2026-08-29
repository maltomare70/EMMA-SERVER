using Dapper;
using EmmaServer.Repositories;
using EmmaServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Costruisce un container di dipendenze con i servizi VERI di EmmaServer (DocService, DocRepository,
/// FornitoriService, ArticoliService, LogService) sostituendo solo due cose:
/// <list type="bullet">
///   <item><see cref="IUserConnectionProvider"/>, che in produzione dipende dall'HttpContext;</item>
///   <item>il primary handler dell'HttpClient di default, cosi' il servizio EMMA-AI non viene chiamato davvero.</item>
/// </list>
/// I documenti creati dai test finiscono davvero sul database, sotto il tenant di test.
/// </summary>
public sealed class DocServiceFixture : IDisposable
{
    private static readonly object _lucchetto = new();
    private static bool _typeHandlerRegistrato;

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public DocServiceFixture()
    {
        RegistraTypeHandlerDapper();

        var configurazione = TestSettings.Configurazione;
        Tenant = TestSettings.Tenant;
        StringaDiConnessione = TestSettings.DatabaseConfigurato
            ? TestSettings.StringaDiConnessione()
            : string.Empty;

        HttpStub = new StubHttpMessageHandler();

        var servizi = new ServiceCollection();

        servizi.AddSingleton<IConfiguration>(configurazione);
        servizi.AddLogging();

        // Il client di default (_httpClientFactory.CreateClient()) passa dallo stub.
        servizi.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => HttpStub);

        servizi.AddSingleton<IUserConnectionProvider>(
            new TestUserConnectionProvider(StringaDiConnessione, Tenant));

        // Tipi qualificati per intero: alcune proprieta' di questa fixture si chiamano come i tipi
        // che stiamo registrando (DocService, DocRepository, FornitoriService).
        servizi.AddScoped(typeof(IRepositoryGenerico<>), typeof(RepositoryGenerico<>));
        servizi.AddScoped<IDocRepository, global::EmmaServer.Repositories.DocRepository>();
        servizi.AddScoped<IFornitoriRepository, global::EmmaServer.Repositories.FornitoriRepository>();
        servizi.AddScoped<IArticoliRepository, global::EmmaServer.Repositories.ArticoliRepository>();
        servizi.AddScoped<IFornitoriService, global::EmmaServer.Services.FornitoriService>();
        servizi.AddScoped<IArticoliService, global::EmmaServer.Services.ArticoliService>();
        servizi.AddScoped<ILogService, global::EmmaServer.Services.LogService>();
        servizi.AddScoped<IDocService, global::EmmaServer.Services.DocService>();

        _provider = servizi.BuildServiceProvider();
        _scope = _provider.CreateScope();

        DocService = _scope.ServiceProvider.GetRequiredService<IDocService>();
        DocRepository = _scope.ServiceProvider.GetRequiredService<IDocRepository>();
        FornitoriService = _scope.ServiceProvider.GetRequiredService<IFornitoriService>();
    }

    /// <summary>Tenant sotto cui vengono create tutte le bolle di prova.</summary>
    public string Tenant { get; }

    public string StringaDiConnessione { get; }

    public StubHttpMessageHandler HttpStub { get; }

    public IDocService DocService { get; }

    public IDocRepository DocRepository { get; }

    public IFornitoriService FornitoriService { get; }

    /// <summary>Conta le righe della tabella docs per il tenant di test: utile per le asserzioni.</summary>
    public async Task<int> ContaDocumentiDelTenantAsync()
    {
        await using var connessione = new NpgsqlConnection(StringaDiConnessione);
        return await connessione.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM docs WHERE tenant = @Tenant", new { Tenant = Tenant });
    }

    /// <summary>Legge lo stato di un documento direttamente dal database.</summary>
    public async Task<int?> LeggiStatoAsync(int idDocumento)
    {
        await using var connessione = new NpgsqlConnection(StringaDiConnessione);
        return await connessione.ExecuteScalarAsync<int?>(
            "SELECT stato FROM docs WHERE id = @Id", new { Id = idDocumento });
    }

    /// <summary>Legge il valore grezzo di una chiave dentro content->'document'.</summary>
    public async Task<string?> LeggiCampoDocumentoAsync(int idDocumento, string chiave)
    {
        await using var connessione = new NpgsqlConnection(StringaDiConnessione);
        return await connessione.ExecuteScalarAsync<string?>(
            "SELECT content->'document'->>@Chiave FROM docs WHERE id = @Id",
            new { Id = idDocumento, Chiave = chiave });
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
        HttpStub.Dispose();
    }
}

/// <summary>
/// Tutte le classi di test condividono la stessa fixture e la stessa collection: xUnit le esegue
/// quindi in sequenza, evitando che due test si pestino i piedi sulle stesse righe del database.
/// </summary>
[CollectionDefinition(NomeCollezioneDatabase.Nome)]
public sealed class DatabaseCollection : ICollectionFixture<DocServiceFixture>
{
    // Classe volutamente vuota: serve solo come punto di aggancio per xUnit.
}

public static class NomeCollezioneDatabase
{
    public const string Nome = "Database EMMA";
}
