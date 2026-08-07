using EmmaServer.Entities;
using EmmaServer.Repositories;
using FatturaElettronica.Ordinaria;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml;



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
    Task CambiaTipoAsync(CambioTipo cambioTipo);
    Task DeleteDocAsync(EmmaDocFilters emmaDocFilter);

    Task<int> CleanDocAsync();

    Task<DocResponse> ImportDocAsync(IFormFile file, string tenant);
    Task<DocResponse> ImportFatturaElettronicaAsync(IFormFile file, string tenant);
}

public class DocService : IDocService
{
    private readonly IDocRepository _repo;
    private readonly IFornitoriService _fornitoriService;
    private readonly IArticoliService _articoliService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogService _logService;
    public DocService(IDocRepository repo, IFornitoriService fornitoriService,
        IArticoliService articoliService, IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogService logService)
    {
        _repo = repo;
        _fornitoriService = fornitoriService;
        _articoliService = articoliService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logService = logService;
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


    public async Task CambiaTipoAsync(CambioTipo cambioTipo)
    {
        await _repo.CambiaTipoAsync(cambioTipo);
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

    public async Task<int> CleanDocAsync()
    {
        return await _repo.CleanDocAsync();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="file"></param>
    /// <param name="tenant"></param>
    /// <returns></returns>
    public async Task<DocResponse> ImportFatturaElettronicaAsync(IFormFile file, string tenant)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        var file_byte = await FileHelper.ConvertFormFileToByteArray(file);

        // Inizializza l'oggetto fattura
        var fattura = new FatturaOrdinaria();

        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Prohibit // Per sicurezza contro attacchi XXE
        };

        using (var stream = file.OpenReadStream())
        using (var xmlReader = XmlReader.Create(stream, settings))
        {
            fattura.ReadXml(xmlReader);
        }


        Guid idMaster = Guid.NewGuid();
        string fornitore = fattura.FatturaElettronicaHeader.CedentePrestatore.DatiAnagrafici.Anagrafica.Denominazione;
        string numeroFattura = fattura.FatturaElettronicaBody[0].DatiGenerali.DatiGeneraliDocumento.Numero;
        DateTime dataFattura = fattura.FatturaElettronicaBody[0].DatiGenerali.DatiGeneraliDocumento.Data;
        decimal totale = fattura.FatturaElettronicaBody[0].DatiGenerali.DatiGeneraliDocumento.ImportoTotaleDocumento ?? 0;

        var righe = new List<ArticoloBolla>();

        // 4. Ciclo sulle righe di dettaglio e sui codici articolo
        foreach (var riga in fattura.FatturaElettronicaBody[0].DatiBeniServizi.DettaglioLinee)
        {
            ArticoloBolla articoloBolla = new ArticoloBolla();

            articoloBolla.UnitaMisura = riga.UnitaMisura;
            articoloBolla.Quantita = riga.Quantita.Value;
            articoloBolla.Descrizione = riga.Descrizione;
            articoloBolla.Codice = riga.CodiceArticolo?.FirstOrDefault()?.CodiceValore ?? string.Empty;
            articoloBolla.Totale = riga.PrezzoTotale;
            articoloBolla.Iva = riga.AliquotaIVA.ToString();
            articoloBolla.Imponibile = 0;
            articoloBolla.Id_Master = idMaster.ToString();
            articoloBolla.Id_Riga = riga.NumeroLinea.ToString();

            righe.Add(articoloBolla);    
            //Console.WriteLine($"[Linea {riga.NumeroLinea}] {riga.Descrizione} - Prezzo: {riga.PrezzoUnitario:C}");
            //foreach (var codice in riga.CodiceArticolo)
            //{
            //    Console.WriteLine($"   Codice ({codice.CodiceTipo}): {codice.CodiceValore}");
            //}
        }


        DdtResponse? ddtResponse = null;

        ddtResponse = new DdtResponse();
        ddtResponse.ModelName = string.Empty;
        ddtResponse.Costs = new Costs();
        ddtResponse.FileName = file.FileName;
        ddtResponse.Document = new DatiBolla()
        {
            DataBolla = dataFattura.ToString("yyyy-MM-dd"),
            Mittente = fornitore,
            Imponibile = 0,
            Iva = "",
            Sconto = "",
            NumeroBolla = numeroFattura,
            TipoDocumento = "4",
            Id = idMaster.ToString(),
            Totale = (double)totale,
            Articoli = righe
        };


        stopwatch.Stop();
        long secondiInteri = stopwatch.ElapsedMilliseconds / 1000;

        EmmaDocFilters emmaDocFilters = new EmmaDocFilters()
        {
            Fornitore = ddtResponse.Document.Mittente,
            NumeroDoc = ddtResponse.Document.NumeroBolla,
            DataDoc = ddtResponse.Document.DataBolla,
            Stato = -1,
            TipoDoc = int.Parse(ddtResponse.Document.TipoDocumento)
        };

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var responseContent = JsonSerializer.Serialize(ddtResponse, options);

        var idDoc = await AddDocAsync(emmaDocFilters,
             responseContent,
            ddtResponse.FileName ?? string.Empty, file_byte, tenant);

        ////--------------------------------------------------------------------------------
        //Aggiorna Anagrafiche
        //--------------------------------------------------------------------------------
        //if (idDoc is not null) await AggiornaAnagrafiche(idDoc.Value);
        ////--------------------------------------------------------------------------------

        return new DocResponse()
        {
            DocId = idDoc is not null ? idDoc.Value : 0,
            DdtResponse = ddtResponse,
        };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="file"></param>
    /// <param name="tenant"></param>
    /// <returns></returns>
    /// <exception cref="ApplicationException"></exception>
    public async Task<DocResponse> ImportDocAsync(IFormFile file, string tenant)
    {

        DdtResponse? ddtResponse = null;

        try
        {        
            Stopwatch stopwatch = Stopwatch.StartNew();

            var file_byte = await FileHelper.ConvertFormFileToByteArray(file);

            //Access the file stream directly (e.g., to upload to AWS S3, Azure Blob, or database)
            using var stream = file.OpenReadStream();

            // 2. Create the HttpClient instance
            var client = _httpClientFactory.CreateClient();

            // 3. Prepare the multipart form data content
            using var form = new MultipartFormDataContent();

            // Open the stream of the incoming file
            using var fileStream = file.OpenReadStream();
            using var streamContent = new StreamContent(fileStream);

            // Pass along the original Content-Type headers
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            // "file" is the parameter name the external API expects. 
            // file.FileName ensures the external API knows the original file name.
            form.Add(streamContent, "file", file.FileName);

            // 4. Send POST request to the external/internal API
            var url = _configuration["EMMA-AI:EndPoint"]; //https://emma-aegc.onrender.com",
            var externalApiUrl = $"{url}/api/v1/doc/ddt";

            using var request = new HttpRequestMessage(HttpMethod.Post, externalApiUrl);
            request.Content = form;

            // ADD YOUR HEADERS HERE
            var model = _configuration["EMMA-AI:Model"];
            request.Headers.Add("x-model", model);
            var apiKey = _configuration["EMMA-AI:ApiKey"];
            request.Headers.Add("X-API-Key", apiKey);

            var response = await client.SendAsync(request);


            stopwatch.Stop();
            long secondiInteri = stopwatch.ElapsedMilliseconds / 1000;

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                // 3. Deserializza la stringa nell'oggetto DatiBolla
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                ddtResponse = JsonSerializer.Deserialize<DdtResponse>(responseContent, options);

                //Salvo sul database
                int? idDoc = 0;
                if (ddtResponse is not null)
                {
                    EmmaDocFilters emmaDocFilters = new EmmaDocFilters()
                    {
                        Fornitore = ddtResponse.Document.Mittente,
                        NumeroDoc = ddtResponse.Document.NumeroBolla,
                        DataDoc = ddtResponse.Document.DataBolla,
                        Stato = -1,
                        TipoDoc = int.Parse(ddtResponse.Document.TipoDocumento)
                    };
                    responseContent = JsonSerializer.Serialize(ddtResponse, options);

                    idDoc = await AddDocAsync(emmaDocFilters,
                         responseContent,
                        ddtResponse.FileName ?? string.Empty, file_byte, tenant);

                    //**************
                    //LOG SUCCESS AI
                    //**************
                    await _logService.AddAsync(new EmmaLog()
                    {
                        stato = 1,
                        tenant = tenant,
                        token_input = ddtResponse.Costs.PromptTokens,
                        token_output = ddtResponse.Costs.OutputTokens,
                        token_tot = ddtResponse.Costs.TotalTokens,
                        cost = ddtResponse.Costs.TotalCostEur,
                        message = idDoc.ToString(),
                        duration = secondiInteri
                    });

                    ////--------------------------------------------------------------------------------
                    //Aggiorna Anagrafiche
                    //--------------------------------------------------------------------------------
                    if (idDoc is not null) await AggiornaAnagrafiche(idDoc.Value);
                    ////--------------------------------------------------------------------------------
                }

                return new DocResponse()
                {
                    DocId = idDoc is not null ? idDoc.Value : 0,
                    DdtResponse = ddtResponse,
                };
            }
            else
            {
                //**************
                //LOG ERRORE AI
                //**************

                await _logService.AddAsync(new EmmaLog()
                {
                    stato = -1,
                    tenant = tenant,
                    token_input = ddtResponse?.Costs?.PromptTokens ?? 0,
                    token_output = ddtResponse?.Costs?.OutputTokens ?? 0,
                    token_tot = ddtResponse?.Costs?.TotalTokens ?? 0,
                    cost = ddtResponse?.Costs?.TotalCostEur ?? 0,
                    message = $"{response.StatusCode} - {response.Content}",
                    duration = secondiInteri
                });

                throw new ApplicationException($"Internal server error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            //**************
            //LOG ERRORE SERVER
            //**************


            await _logService.AddAsync(new EmmaLog()
            {
                stato = -2,
                tenant = tenant,
                token_input = ddtResponse?.Costs?.PromptTokens ?? 0,
                token_output = ddtResponse?.Costs?.OutputTokens ?? 0,
                token_tot = ddtResponse?.Costs?.TotalTokens ?? 0,
                cost = ddtResponse?.Costs?.TotalCostEur ?? 0,
                message = ex.Message,
                duration = 0
            });

            throw new ApplicationException($"Internal server error: {ex.Message}");
        }
    }

    private async Task AggiornaAnagrafiche(int idDoc)
    {
        try
        {
            await AddOrUpdateFornitorieArticoli(idDoc);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}