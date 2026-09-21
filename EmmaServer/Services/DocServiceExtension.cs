using EmmaServer.Entities.Dtos;
using EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IDocServiceExtension
{
    Task ChiusuraAutomaticaDocumenti();

    Task ChiusuraAutomaticaDocumento(string Id_Master, string tipoDocumento, string tenant);
}
public class DocServiceExtension : IDocServiceExtension
{
    private readonly IDocRepository _repo;

    private readonly IConfiguration _configuration;

    private readonly IConciliaRigheService _conciliaRigheService;

    public DocServiceExtension(IDocRepository repo, IConfiguration configuration, IConciliaRigheService conciliaRigheService)
    {
        _repo = repo;

        _configuration = configuration;

        _conciliaRigheService = conciliaRigheService;

    }

    public async Task ChiusuraAutomaticaDocumenti()
    {
        var docs = await _repo.GetDocsAsync(new EmmaDocFilters()
        {
            Stato = 0
        });

        foreach (var doc in docs)
        {
            if (doc is not null)
            {
                var vdoc = doc.ToDoc();

                if (vdoc is null) continue;

                var first = vdoc.Articoli.FirstOrDefault();
                if (first is null) continue;

                await ChiusuraAutomaticaDocumento(first.Id_Master, vdoc.TipoDocumento, doc.tenant);
            }
        }
    }

    public async Task ChiusuraAutomaticaDocumento(string Id_Master, string tipoDocumento, string tenant)
    {
        var righe = await _conciliaRigheService.GetRigheConciliazioneAsync(Id_Master, string.Empty, tenant);

        if (righe.Any())
        {
            var totalsByTipo = righe.GroupBy(r => r.tipo_doc).Select(g => new { TipoDoc = g.Key, TotaleDelta = g.Sum(x => x.delta) }).ToList();

            //ORDINI
            if (tipoDocumento == "1" && totalsByTipo.Count == 1)
            {
                if (totalsByTipo[0].TotaleDelta == 0)
                {
                    await _repo.CambiaStatoAsync(new CambioStato()
                    {
                        Id = Id_Master,
                        Stato = 1
                    });
                }
            }

            //DDT
            if (tipoDocumento == "2" && totalsByTipo.Count == 2)
            {
                if (totalsByTipo[0].TotaleDelta == 0 && totalsByTipo[1].TotaleDelta == 0)
                {
                    await _repo.CambiaStatoAsync(new CambioStato()
                    {
                        Id = Id_Master,
                        Stato = 1
                    });
                }
            }


            //FATTURE
            if ((tipoDocumento == "3" || tipoDocumento == "4") && totalsByTipo.Count == 1)
            {
                if (totalsByTipo[0].TotaleDelta == 0)
                {
                    await _repo.CambiaStatoAsync(new CambioStato()
                    {
                        Id = Id_Master,
                        Stato = 1
                    });
                }
            }
        }

    }
}