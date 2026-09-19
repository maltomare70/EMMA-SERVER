namespace EmmaServer.DataSeed.Tests.Infrastructure;

/// <summary>
/// Sostituisce <c>UserConnectionProvider</c>, che in produzione legge il tenant dai claim
/// dell'HttpContext. Qui tenant e stringa di connessione arrivano da <see cref="SeedSettings"/>.
/// </summary>
public sealed class SeedUserConnectionProvider : IUserConnectionProvider
{
    private readonly string _stringaDiConnessione;
    private readonly string _tenant;

    public SeedUserConnectionProvider(string stringaDiConnessione, string tenant)
    {
        _stringaDiConnessione = stringaDiConnessione;
        _tenant = tenant;
    }

    public string GetEmmaConnectionString() => _stringaDiConnessione;

    public string GetConnectionStringPostresSQL() => _stringaDiConnessione;

    public string GetTenant() => _tenant;
}
