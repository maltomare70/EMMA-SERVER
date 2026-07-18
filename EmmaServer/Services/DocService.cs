using EmmaServer.Entities;
using  EmmaServer.Repositories;
using System.Text.Json;


namespace EmmaServer.Services;

public interface IDocService
{
    Task<int?> AddAsync(EmmaDoc doc);
    Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters emmaDocFilters);
    Task<bool?> DeleteAsync(EmmaDoc doc);
    Task<int?> AddDocAsync(EmmaDocFilters emmaDocFilters,
        string json, string fileName, byte[] file_byte, string tenant);

    Task AddOrUpdateFornitorieArticoli(int docId);
    Task InsertRigaDocAsync(ArticoloBolla articoloBolla);
    Task UpdateRigaDocAsync(ArticoloBolla articoloBolla);
    Task DeleteRigaDocAsync(ArticoloBolla articoloBolla);
    Task<bool> UpdateAsync(EmmaDoc doc);
    Task CambiaStatoAsync(CambioStato cambioStato);
    Task DeleteDocAsync(EmmaDocFilters emmaDocFilter);
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

    public async Task<bool> UpdateAsync(EmmaDoc doc)
    {
        return await _repo.UpdateAsync(doc);
    }
    
    public async Task CambiaStatoAsync(CambioStato cambioStato)
    {
        await _repo.CambiaStatoAsync(cambioStato);
    }
    
    
    public async Task InsertRigaDocAsync(ArticoloBolla articoloBolla)
    {
        await _repo.InsertRigaDocAsync(articoloBolla);
    }

    
    public async Task UpdateRigaDocAsync(ArticoloBolla articoloBolla)
    {
        await _repo.UpdateRigaDocAsync(articoloBolla);
    }

    
    public async Task<bool?> DeleteAsync(EmmaDoc doc)
    {
        return await _repo.DeleteAsync(doc);
    }

    public async Task DeleteRigaDocAsync(ArticoloBolla articoloBolla)
    {
        await _repo.DeleteRigaDocAsync(articoloBolla);
    }
    
    public async Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters emmaDocFilters)
    {
        return await _repo.GetDocsAsync(emmaDocFilters);
    }

    public async Task DeleteDocAsync(EmmaDocFilters emmaDocFilter)
    {
        var doclist = await GetDocsAsync(emmaDocFilter);
        if (doclist?.Count > 0)
        {
            var doc = doclist.FirstOrDefault();
            if (doc is not null) await DeleteAsync(doc);
        }
    }
    
    public async Task<int?> AddDocAsync(EmmaDocFilters emmaDocFilter, string json, 
        string fileName, byte[] file_byte, string tenant)
    {
        var doclist = await GetDocsAsync(emmaDocFilter);
        if (doclist?.Count > 0)
        {
            var doc = doclist.FirstOrDefault();
            if (doc is not null)
            {
                if (doc.stato == 0)
                    await DeleteAsync(doc);
                else
                    throw new Exception($"Documento {doc.ToDoc()?.TipoDocumento} - {doc.ToDoc()?.Mittente} - {doc.ToDoc()?.NumeroBolla} - {doc.ToDoc()?.DataBolla} già chiuso");                                    
            }
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