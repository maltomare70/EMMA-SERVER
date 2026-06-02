using EmmaServer.Repositories;
using EmmaServer.Entities;

namespace EmmaServer.Services;

public interface IBolleMasterService
{
    Task<int?> AddAsync(int id, DatiBolla dati_bolla);
    Task DeleteAsync(BolleMaster bolleMaster);
}
    
public class BolleMasterService : IBolleMasterService
{
    private readonly  IRepositoryGenerico<BolleMaster>  _bolleMasterRepository; 
    private readonly  IRepositoryGenerico<BolleRows>  _bolleRowsRepository;
    private readonly IBolleRowsRepository _rowsRepository;
    
    public BolleMasterService(IRepositoryGenerico<BolleMaster> bolleMasterRepository,
        IRepositoryGenerico<BolleRows> bolleRowsRepository, IBolleRowsRepository rowsRepository)
    {
        _bolleMasterRepository =  bolleMasterRepository;
        _bolleRowsRepository = bolleRowsRepository;
        _rowsRepository = rowsRepository;
    }

    public async Task<int?> AddAsync(int id, DatiBolla dati_bolla)
    {
        int? id_master = 0;
        try
        {
            id_master = await _bolleMasterRepository.AddAsync(new BolleMaster()
            {
                id_bolla = id,
                fornitore = dati_bolla.Mittente,
                numero_bolla = dati_bolla.NumeroBolla,
                data_bolla = dati_bolla.DataBolla,
                tipo_doc = Int32.Parse(dati_bolla.TipoDocumento),
                imponibile = dati_bolla.Imponibile,
                totale = dati_bolla.Totale
            });

            foreach (var articoloBolla in dati_bolla.Articoli)
            {
                var row = new BolleRows()
                {
                    id_bolla = id_master.Value,
                    codice =  articoloBolla.Codice,
                    descrizione =  articoloBolla.Descrizione,
                    iva = articoloBolla.Iva,
                    qta = articoloBolla.Quantita,
                    totale = articoloBolla.Totale,
                    imponibile = articoloBolla.Imponibile,
                    um = articoloBolla.UnitaMisura
                };
                _ = await _bolleRowsRepository.AddAsync(row);
            }

            return id_master;
        }
        catch (Exception e)
        {
            await _rowsRepository.DeleteRowsByMaster(id_master.Value);
            throw;
        }
    }

    public async Task DeleteAsync(BolleMaster bolleMaster)
    {
        await _rowsRepository.DeleteRowsByMaster(bolleMaster.id);

        await _bolleMasterRepository.DeleteAsync(bolleMaster);
    }
}