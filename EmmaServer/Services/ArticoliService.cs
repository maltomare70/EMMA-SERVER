using EmmaServer.Entities;
using EmmaServer.Repositories;
using System.Text.Json;
using FuzzySharp;


namespace EmmaServer.Services;

public interface IArticoliService
{
    Task<EmmaArticoli?> GetArticoloAsync(int id);
    Task<int?> AddArticoloAsync(EmmaArticoli articolo);
    Task<List<int?>> AddArticoliAsync(List<EmmaArticoli> articolo);
    Task<bool?> UpdateFornitoreAsync(EmmaArticoli articolo);
    Task<IEnumerable<EmmaArticoli?>> GetAllTenantAsync(string descrizione);
    Task AddOrUpdateArticoliByDocIdAsync(int docId, int idFornitore);


}

public class ArticoliService : IArticoliService
{
    private readonly IUserConnectionProvider _connectionProvider;
    private readonly IArticoliRepository _repository;
    private readonly IDocRepository _docRepository;
    private readonly IFornitoriService _fornitoriService;
    public ArticoliService(IUserConnectionProvider connectionProvider, 
        IArticoliRepository  repository, IDocRepository  docRepository, IFornitoriService fornitoriService)
    {
        _connectionProvider = connectionProvider;
        _repository = repository;
        _docRepository = docRepository;
        _fornitoriService = fornitoriService;
    }
    
    public async  Task<EmmaArticoli?> GetArticoloAsync(int id)
    {
        return await _repository.GetIdAsync(id);
    }

    public async Task<int?> AddArticoloAsync(EmmaArticoli articolo)
    {
        var tenant = _connectionProvider.GetTenant();
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is null or empty");

        if (articolo.idfornitore == 0) throw new ArgumentException("Id Fornitoe is null or empty");
        
        EmmaArticoli emmaArticoli = new EmmaArticoli()
        {
            codice = articolo.codice,
            descrizione = articolo.descrizione,
            rifcodice =  articolo.rifcodice,
            rifdescrizione =  articolo.rifdescrizione,
            scorecodice = articolo.scorecodice,
            scoredescrizione = articolo.scoredescrizione,
            idfornitore = articolo.idfornitore,
            tenant = tenant
        };
        return await _repository.AddAsync(emmaArticoli);
    }

    public async Task<List<int?>> AddArticoliAsync(List<EmmaArticoli> articolo)
    {
        List<int?> list = new List<int?>();
        
        foreach (var emmaArticoli in articolo)
        {
            var ret = await AddArticoloAsync(emmaArticoli);
            list.Add(ret.Value);
        }

        return list;
    }

    public async Task<bool?> UpdateFornitoreAsync(EmmaArticoli articolo)
    {
        var tenant = _connectionProvider.GetTenant();
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is null or empty");

        articolo.tenant = tenant;

        return await _repository.UpdateAsync(articolo);
    }

    public async Task<IEnumerable<EmmaArticoli?>> GetAllTenantAsync(string descrizione)
    {
        var tenant = _connectionProvider.GetTenant();
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is null or empty");
        
        var articoli = await _repository.GetAllTenantAsync(tenant);

        var fornitori = await _fornitoriService.GetAllTenantAsync();
        var fonritore = fornitori.FirstOrDefault(x => x!.descrizione.Equals(descrizione, StringComparison.InvariantCultureIgnoreCase));
        var fornitoreId = fonritore?.id;
        
        return articoli.Where(x=> x.idfornitore == fornitoreId).ToList();
    }


    public async Task AddOrUpdateArticoliByDocIdAsync(int docId, int idFornitore)
    {
        var doc = await _docRepository.GetIdAsync(docId);
        if (doc is not null)
        {
            var tenant = _connectionProvider.GetTenant();
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            DdtResponse? ddtResponse = JsonSerializer.Deserialize<DdtResponse>(doc.content!, options);
            var articoli = ddtResponse?.Document.Articoli;
            
            var listaArticoli = await _repository.GetAllTenantByFornitoreAsync(tenant, idFornitore);
            
            var codiceList = listaArticoli.Select(f => f.codice).ToList();
            foreach (var articoloBolla in articoli)
            {
                var bestMatch = Process.ExtractOne(articoloBolla.Codice, codiceList);
                if (bestMatch != null)
                {
                    string matchedValue = bestMatch.Value; // The string that matched
                    int score = bestMatch.Score;           // Match score (0-100)
                    if (score == 100)
                    {
                        //E' già inserito sul database
                    }
                    else if (score >= 90 && score < 100)
                    {
                        //Potrebbe essere lui
                        //da valutare se l'algoritmo è ok
                        //da loggare
                    }
                    else
                    {
                        //ADD
                        await AddArticoloAsync(new  EmmaArticoli()
                        {
                            codice = articoloBolla.Codice,
                            descrizione = articoloBolla.Descrizione,
                            tenant = tenant,
                            idfornitore = idFornitore,
                            scorecodice = score,
                        });
                    }
                }
                else
                {
                    //ADD
                    await AddArticoloAsync(new  EmmaArticoli()
                    {
                        codice = articoloBolla.Codice,
                        descrizione = articoloBolla.Descrizione,
                        tenant = tenant,
                        idfornitore = idFornitore,
                        scorecodice = 100
                    });
                }
            }
            
            // var descrizioneList = listaArticoli.Select(f => f.descrizione).ToList();
            // foreach (var articoloBolla in articoli)
            // {
            //     var bestMatch = Process.ExtractOne(articoloBolla.Descrizione, descrizioneList);
            //     if (bestMatch != null)
            //     {
            //         string matchedValue = bestMatch.Value; // The string that matched
            //         int score = bestMatch.Score;           // Match score (0-100)
            //         if (score == 100)
            //         {
            //             //E' già inserito sul database
            //         }
            //         else if (score >= 90 && score < 100)
            //         {
            //             //Potrebbe essere lui da valutare se l'algoritmo è ok da loggare
            //         }
            //         else
            //         {
            //             //ADD
            //             await AddArticoloAsync(new  EmmaArticoli()
            //             {
            //                 codice = articoloBolla.Codice,
            //                 descrizione = matchedValue,
            //                 tenant = tenant,
            //                 idfornitore = idFornitore,
            //                 scoredescrizione = score
            //             });
            //         }
            //     }
            //     else
            //     {
            //         //ADD
            //         await AddArticoloAsync(new  EmmaArticoli()
            //         {
            //             codice = articoloBolla.Codice,
            //             descrizione = articoloBolla.Descrizione,
            //             tenant = tenant,
            //             idfornitore = idFornitore,
            //             scoredescrizione = 100     
            //         });
            //     }
            // }
        }
    }
}