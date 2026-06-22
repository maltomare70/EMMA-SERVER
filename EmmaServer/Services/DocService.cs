using EmmaServer.Entities;
using  EmmaServer.Repositories;
using System.Text.Json;


namespace EmmaServer.Services;

public interface IDocService
{
    Task<int?> AddAsync(EmmaDoc doc);
    Task<List<EmmaDoc?>> GetDocByFornitore(string fornitore);
    Task<EmmaDoc?> GetDocAsync(string fornitore, string numero_doc, string dataa_doc);
    Task<bool?> DeleteAsync(EmmaDoc doc);
    Task<int?> AddDocAsync(string fornitore, string numero_doc, string data_doc,
        string json, string fileName, byte[] file_byte, string tenant);

    Task AddOrUpdateFornitorieArticoli(int docId);
}

public class DocService : IDocService
{
    private readonly IDocRepository _repo;
    private readonly IFornitoriService _fornitoriService;
    private readonly IArticoliService _articoliService;
    public DocService(IDocRepository repo, IFornitoriService fornitoriService,
        IArticoliService articoliService)
    {
        _repo = repo;
        _fornitoriService = fornitoriService;
        _articoliService = articoliService; 
    }

    public async Task AddOrUpdateFornitorieArticoli(int docId)
    {
        int idFornitore = await _fornitoriService.AddOrUpdateFornitoriByDocIdAsync(docId);
        await _articoliService.AddOrUpdateArticoliByDocIdAsync(docId, idFornitore);
    }
    
    public async Task<int?> AddAsync(EmmaDoc doc)
    {
        return await _repo.AddAsync(doc);
    }
    
    public async Task<bool?> DeleteAsync(EmmaDoc doc)
    {
        return await _repo.DeleteAsync(doc);
    }

    public async Task<List<EmmaDoc?>> GetDocByFornitore(string fornitore)
    {
        return await _repo.GetDocsByFornitore(fornitore);
    }
    
    public async Task<EmmaDoc?> GetDocAsync(string fornitore, string numero_doc, string data_doc)
    {
        return await _repo.GetDocAsync( fornitore,  numero_doc,  data_doc);
    }
    
    public async Task<int?> AddDocAsync(string fornitore, string numero_bolla, string data_bolla, string json, 
        string fileName, byte[] file_byte, string tenant)
    {
        var doc = await GetDocAsync(fornitore, numero_bolla,  data_bolla);
        if (doc is not null) await DeleteAsync(doc);
        //inserisco
        return await AddAsync((new EmmaDoc()
        {
            file_name = fileName,
            content = JsonDocument.Parse(json),
            allegato = file_byte,
            tenant = tenant
        }));
    }
}