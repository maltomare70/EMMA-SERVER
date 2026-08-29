namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Sostituisce <see cref="UserConnectionProvider"/> nei test.
///
/// Quello di produzione legge il tenant dai claim dell'HttpContext: fuori da una richiesta HTTP
/// solleverebbe sempre eccezione. Qui tenant e stringa di connessione arrivano dalla configurazione
/// dei test (vedi <see cref="TestSettings"/>).
/// </summary>
public sealed class TestUserConnectionProvider : IUserConnectionProvider
{
    private readonly string _stringaDiConnessione;
    private readonly string _tenant;

    public TestUserConnectionProvider(string stringaDiConnessione, string tenant)
    {
        _stringaDiConnessione = stringaDiConnessione;
        _tenant = tenant;
    }

    public string GetEmmaConnectionString() => _stringaDiConnessione;

    public string GetConnectionStringPostresSQL() => _stringaDiConnessione;

    public string GetTenant() => _tenant;
}
