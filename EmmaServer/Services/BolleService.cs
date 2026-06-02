using EmmaServer.Entities;
using  EmmaServer.Repositories;
using System.Text.Json;


namespace EmmaServer.Services;

public interface IBolleService
{
    Task<int?> AddAsync(Bolle user);
    Task<List<Bolle?>> GetBolleByFornitore(string fornitore);
    Task<Bolle?> GetBollaAsync(string fornitore, string numeroBolla, string dataBolla);
    Task<bool?> DeleteAsync(Bolle bolla);
    Task<int?> AddBollaAsync(string fornitore, string numero_bolla, string data_bolla, string json, string fileName, byte[] file_byte);
}

public class BolleService : IBolleService
{
    private readonly IBolleRepository _repo;

    public BolleService(IBolleRepository repo)
    {
        _repo = repo;
    }
    
    public async Task<int?> AddAsync(Bolle bolla)
    {
        return await _repo.AddAsync(bolla);
    }
    
    public async Task<bool?> DeleteAsync(Bolle bolla)
    {
        return await _repo.DeleteAsync(bolla);
    }

    public async Task<List<Bolle?>> GetBolleByFornitore(string fornitore)
    {
        return await _repo.GetBolleByFornitore(fornitore);
    }
    
    public async Task<Bolle?> GetBollaAsync(string fornitore, string numeroBolla, string dataBolla)
    {
        return await _repo.GetBollaAsync( fornitore,  numeroBolla,  dataBolla);
    }
    
    public async Task<int?> AddBollaAsync(string fornitore, string numero_bolla, string data_bolla, string json, string fileName, byte[] file_byte)
    {
        var bolla = await GetBollaAsync(fornitore, numero_bolla,  data_bolla);
        if (bolla is not null) await DeleteAsync(bolla);
        //inserisco
        return await AddAsync((new Bolle()
        {
            file_name = fileName,
            data = JsonDocument.Parse(json),
            allegato = file_byte
        }));
    }
}