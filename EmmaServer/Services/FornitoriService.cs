using EmmaServer.Entities;
using EmmaServer.Repositories;
using System.Text.Json;
using FuzzySharp;


namespace EmmaServer.Services;

public interface IFornitoriService
{
    Task<EmmaFornitori?> GetFornitoreAsync(int id);
    Task<int> AddFornitoreAsync(EmmaFornitori fornitore);
    Task<bool?> UpdateFornitoreAsync(EmmaFornitori fornitore);
    Task<IEnumerable<EmmaFornitori?>> GetAllTenantAsync();
    Task<int> AddOrUpdateFornitoriByDocIdAsync(int docId);

}

public class FornitoriService : IFornitoriService
{
    private readonly IUserConnectionProvider _connectionProvider;
    private readonly IFornitoriRepository _repository;
    private readonly IDocRepository _docRepository;
    
    public FornitoriService(IUserConnectionProvider connectionProvider, 
        IFornitoriRepository  repository, IDocRepository  docRepository)
    {
        _connectionProvider = connectionProvider;
        _repository = repository;
        _docRepository = docRepository;
    }

    public async Task<int> AddFornitoreAsync(EmmaFornitori fornitore)
    {
        var tenant = _connectionProvider.GetTenant();
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is null or empty");

        EmmaFornitori emmaFornitori = new EmmaFornitori()
        {
            descrizione = fornitore.descrizione,
            riferimento =  fornitore.riferimento,
            tenant =  tenant,
            score = fornitore.score
        };
        return await _repository.AddAsync(emmaFornitori);
    }

    public async Task<int> AddOrUpdateFornitoriByDocIdAsync(int docId)
    {
        int id_fornitore = 0;
        
        var doc = await _docRepository.GetIdAsync(docId);
        if (doc is not null)
        {
            //Fornitore all'interno del documento
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            DdtResponse? ddtResponse = JsonSerializer.Deserialize<DdtResponse>(doc.content!, options);
            var fornitore = ddtResponse?.Document.Mittente;
            
            //Elenco di tutti i fornitori del tenant
            var fornitori = await GetAllTenantAsync();
            var fornitoriList = fornitori.Select(f => f.descrizione).ToList();
            
            //Cerco il fornitore dall'elenco
            var bestMatch = Process.ExtractOne(fornitore, fornitoriList);
            if (bestMatch != null)
            {
                string matchedValue = bestMatch.Value; // The string that matched
                int score = bestMatch.Score;           // Match score (0-100)
                int index = bestMatch.Index;           // Index in the original collection

                var f = fornitori.ToList()[index];
                
                if (score == 100)
                {
                    //E' già inserito sul database
                    id_fornitore = f.id;
                }
                else if (score >= 90 && score < 100)
                {
                    //Potrebbe essere lui da valutare se l'algoritmo è ok da loggare
                    id_fornitore = f.id;
                }
                else
                {
                    id_fornitore = await AddFornitoreAsync(new EmmaFornitori()
                    {
                        tenant = doc.tenant,
                        descrizione = fornitore,
                        score = score,
                    });
                }
            }
            else
            {
                id_fornitore = await AddFornitoreAsync(new EmmaFornitori()
                {
                    tenant = doc.tenant,
                    descrizione = fornitore,
                    score = 100
                });
            }
        }

        return id_fornitore;
    }
    
    public async Task<bool?> UpdateFornitoreAsync(EmmaFornitori fornitore)
    {
        var tenant = _connectionProvider.GetTenant();
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is null or empty");
        
        EmmaFornitori emmaFornitori = new EmmaFornitori()
        {
            descrizione = fornitore.descrizione,
            riferimento =  fornitore.riferimento,
            tenant =  tenant
        };
        return await _repository.UpdateAsync(emmaFornitori);
    }

    public async Task<EmmaFornitori?> GetFornitoreAsync(int id)
    {
        return await _repository.GetIdAsync(id);
    }
    
    public async Task<IEnumerable<EmmaFornitori?>> GetAllTenantAsync()
    {
        var tenant = _connectionProvider.GetTenant();
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is null or empty");
        
        return await _repository.GetAllTenantAsync(tenant);
    }
    
}