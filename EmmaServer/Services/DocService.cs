using EmmaServer.Entities;
using  EmmaServer.Repositories;
using System.Text.Json;


namespace EmmaServer.Services;

public interface IDocService
{
    Task<int?> AddAsync(EmmaDoc doc);
    //Task<List<EmmaDoc?>> GetDocByFornitore(string fornitore);
    //Task<EmmaDoc?> GetDocAsync(EmmaDocFilters emmaDocFilters);
    Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters emmaDocFilters);
    Task<bool?> DeleteAsync(EmmaDoc doc);
    Task<int?> AddDocAsync(EmmaDocFilters emmaDocFilters,
        string json, string fileName, byte[] file_byte, string tenant);

    Task AddOrUpdateFornitorieArticoli(int docId);
    Task UpdateRigaDoc(ArticoloBolla articoloBolla);
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
    
    public async Task UpdateRigaDoc(ArticoloBolla articoloBolla)
    {
        await _repo.UpdateRigaDocAsync(articoloBolla);
    }

    
    public async Task<bool?> DeleteAsync(EmmaDoc doc)
    {
        return await _repo.DeleteAsync(doc);
    }

    // public async Task<List<EmmaDoc?>> GetDocByFornitore(string fornitore)
    // {
    //     return await _repo.GetDocsByFornitore(fornitore);
    // }
    

    
    public async Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters emmaDocFilters)
    {
        return await _repo.GetDocsAsync(emmaDocFilters);
    }
    
    public async Task<int?> AddDocAsync(EmmaDocFilters emmaDocFilter, string json, 
        string fileName, byte[] file_byte, string tenant)
    {
        var doclist = await GetDocsAsync(emmaDocFilter);
        if (doclist?.Count == 1)
        {
            var doc = doclist.FirstOrDefault();
            if (doc is not null) await DeleteAsync(doc);
        }

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