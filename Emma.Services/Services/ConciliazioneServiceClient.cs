using Emma.Services.Http;
using EmmaServer.Entities;

namespace EmmaClientAv.Services;

public interface IConciliazioneServiceClient
{
    Task<List<ConciliazioneResponse>> GetConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture);

    Task SalvaConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture);

    Task<List<EmmaConciliaRighe>> GetAllAsync();

    Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster);
    Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga);
}

public class ConciliazioneServiceClient : ServiceClientBase, IConciliazioneServiceClient
{
    private const string Endpoint = "/api/v1/conciliazione";
    private const string EndpointSalva = "/api/v1/salva-conciliazione";
    private const string EndpointMasterRiga = Endpoint + "/master/{0}/riga/{1}";
    private const string EndpointMaster = Endpoint + "/master/{0}";

    public ConciliazioneServiceClient(string url, string user, string password, string tenant = "")
        : base(url, user, password, tenant)
    {
    }

    public ConciliazioneServiceClient(HttpClient httpClient, string url, string user, string password, string tenant = "")
        : base(httpClient, url, user, password, tenant)
    {
    }

    /// <summary>In caso di errore restituisce una lista vuota, non lancia.</summary>
    public async Task<List<EmmaConciliaRighe>> GetAllAsync()
        => await TryGetAsync<List<EmmaConciliaRighe>>(Endpoint).ConfigureAwait(false) ?? new List<EmmaConciliaRighe>();

    /// <summary>
    /// Restituisce le righe di conciliazione per uno specifico idMaster/idRiga.
    /// In caso di errore restituisce una lista vuota, non lancia.
    /// </summary>
    public async Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga)
    {
        var path = string.Format(
            EndpointMasterRiga,
            Uri.EscapeDataString(idMaster ?? string.Empty),
            Uri.EscapeDataString(idRiga ?? string.Empty));

        return await TryGetAsync<List<EmmaConciliaRigheDto>>(path).ConfigureAwait(false)
               ?? new List<EmmaConciliaRigheDto>();
    }

    public async Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster)
    {
        var path = string.Format(
            EndpointMaster,
            Uri.EscapeDataString(idMaster ?? string.Empty));

        return await TryGetAsync<List<EmmaConciliaRigheDto>>(path).ConfigureAwait(false)
               ?? new List<EmmaConciliaRigheDto>();
    }

    /// <summary>
    /// Esegue una conciliazione per ogni fornitore presente in bolle o fatture,
    /// saltando i fornitori senza righe da confrontare.
    /// </summary>
    public async Task<List<ConciliazioneResponse>> GetConciliazione(
        List<RigaConciliazione> bolle,
        List<RigaConciliazione> fatture)
    {
        var results = new List<ConciliazioneResponse>();

        List<string?> fornitoriValidi = bolle.Select(b => b.Fornitore)
               .Union(fatture.Select(f => f.Fornitore))
               .Where(f => !string.IsNullOrEmpty(f))
               .Distinct()
               .ToList();

        foreach (var fornitore in fornitoriValidi)
        {
            if (string.IsNullOrWhiteSpace(fornitore)) continue;

            List<DettaglioDocumento> bolleInput = ToDettagli(bolle, fornitore);
            List<DettaglioDocumento> fattureInput = ToDettagli(fatture, fornitore);

            // Salta il fornitore se non ci sono né bolle né fatture
            if (bolleInput.Count == 0 && fattureInput.Count == 0) continue;

            var inputConciliazione = new InputConciliazione
            {
                Fornitore = fornitore,
                Bolle = bolleInput,
                Fatture = fattureInput,
            };

            var fuzzyMatchResult = await PostAsync<ConciliazioneResponse>(Endpoint, inputConciliazione)
                .ConfigureAwait(false);

            if (fuzzyMatchResult != null)
                results.Add(fuzzyMatchResult);
        }

        return results;
    }

    public Task SalvaConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture)
    {
        var payload = new PayloadRiconciliazione
        {
            codice = Guid.NewGuid().ToString(),
            bolle = bolle,
            fatture = fatture
        };

        return PostAsync(EndpointSalva, payload);
    }

    private static List<DettaglioDocumento> ToDettagli(List<RigaConciliazione> righe, string fornitore)
        => righe
            .Where(r => r.Fornitore == fornitore)
            .Select(r => new DettaglioDocumento
            {
                Id = r.IdRiga ?? string.Empty,
                Codice = r.CodiceArticolo ?? string.Empty,
                Qta = r.Qta
            })
            .ToList();
}
