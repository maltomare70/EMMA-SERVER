using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;



namespace EmmaClientAv.Services;

public interface IDocService
{
    Task<List<EmmaDoc>> GetDocsAsync(EmmaDocFilters docFilters);
    Task CambioStato(MasterDocumento masterDocumento);
    Task CancellaDocumento(MasterDocumento masterDocumento);
    Task<bool> InviaAddAllApi(RigheDocumento riga);
    Task InviaModificaAllApi(ArticoloBolla articoloBolla);
    Task<bool> InviaEliminazioneAllApi(RigheDocumento riga);
    Task<bool> PingAsync();
    Task<DatiBolla?> InviaFileAsync(Stream fileStream, string fileName);
}

public class DocService :IDocService
{
    private readonly HttpClient Client;

    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    private readonly string _tenant;
    public DocService(string url, string user, string password, string tenant = "")
    {
        _url = url;
        _user = user;
        _password = password;
        _tenant= tenant;

        Client = new HttpClient();
    }

    //Serve per forzare l'avvio del server nella versione free
    //poi questa chiamata va eliminata che non serve
    public async Task<bool> PingAsync()
    {
        string urlApi = $"https://emma-aegc.onrender.com/api/health";
        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode) return true;
        else return false;
    }

    public async Task<List<EmmaDoc>> GetDocsAsync(EmmaDocFilters docFilters)
    {
        string urlApi = $"{_url}/api/v1/doc";
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(docFilters);
        
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var emmaDocList = await response.Content.ReadFromJsonAsync<List<EmmaDoc>>();
            return emmaDocList ?? new List<EmmaDoc>();
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }

    public async Task CambioStato(MasterDocumento masterDocumento)
    {
        string urlApi = $"{_url}/api/v1/doc/stato";
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(new CambioStato()
        {
            Id = masterDocumento.Id,
            Stato = masterDocumento.StatoDocumento == "Aperto" ? 1 : 0
        });
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            //
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }
    
    private int GetTipoDocumento(string tipodoc)
    {
        return int.Parse(tipodoc);
    }

    public async Task CancellaDocumento(MasterDocumento masterDocumento)
    {
        EmmaDocFilters emmaDocFilters = new()
        {
            Fornitore =  masterDocumento.Fornitore,
            NumeroDoc = masterDocumento.NumeroDocumento,
            DataDoc = masterDocumento.DataDocumento,
            TipoDoc = GetTipoDocumento(masterDocumento.TipDocumento),
            Stato = masterDocumento.StatoDocumento == "Aperto" ? 0 : 1
        };
        
        string urlApi = $"{_url}/api/v1/doc";
        using var request = new HttpRequestMessage(HttpMethod.Delete, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(emmaDocFilters);
        
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            //
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }
    
    public async Task<bool> InviaAddAllApi(RigheDocumento riga)
    {
     
        var articoloBolla = new ArticoloBolla();
        articoloBolla.Id_Master = riga.IdMaster;
        articoloBolla.Id_Riga = Guid.NewGuid().ToString();
        articoloBolla.Quantita = riga.Qta;
        articoloBolla.Descrizione = riga.DescrizioneArticolo;
        articoloBolla.Codice = riga.CodiceArticolo;
        articoloBolla.Imponibile = riga.Imponibile;
        articoloBolla.Totale = riga.Totale;
        articoloBolla.UnitaMisura = riga.UnitaMisura;
        articoloBolla.Iva = riga.IVA;
    
        string urlApi = $"{_url}/api/v1/doc/riga";
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
    
        request.Content = JsonContent.Create(articoloBolla);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
           return true;
        }
        else
        {
            return false;
        }
    }
    
    public async Task InviaModificaAllApi(ArticoloBolla articoloBolla)
    {
        string urlApi = $"{_url}/api/v1/doc/riga";
        using var request = new HttpRequestMessage(HttpMethod.Put, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            
        request.Content = JsonContent.Create(articoloBolla);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }

        return;
    }
    
    public async Task<bool> InviaEliminazioneAllApi(RigheDocumento riga)
    {
        try
        {
            var articoloBolla = new ArticoloBolla();
            articoloBolla.Id_Master = riga.IdMaster;
            articoloBolla.Id_Riga = riga.IdRiga;
            articoloBolla.Quantita = riga.Qta;
            articoloBolla.Descrizione = riga.DescrizioneArticolo;
            articoloBolla.Codice = riga.CodiceArticolo;
            articoloBolla.Imponibile = riga.Imponibile;
            articoloBolla.Totale = riga.Totale;
            articoloBolla.UnitaMisura = riga.UnitaMisura;
            articoloBolla.Iva = riga.IVA;
        
            string urlApi = $"{_url}/api/v1/doc/riga";
            using var request = new HttpRequestMessage(HttpMethod.Delete, urlApi);
            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        
            request.Content = JsonContent.Create(articoloBolla);
            HttpResponseMessage response = await Client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<DatiBolla?> InviaFileAsync(Stream fileStream, string fileName)
    {
        string urlApi = $"{_url}/api/v1/doc";

        using var client = new HttpClient();
        using var content = new MultipartFormDataContent();
        
        // 1. Apriamo lo stream del file in lettura
        using var streamContent = new StreamContent(fileStream);

        // 2. Impostiamo l'header del tipo di contenuto (opzionale, ma consigliato)
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // 3. Il nome del parametro ("file") DEVE corrispondere esattamente 
        // al nome della variabile nell'API Python: `file: UploadFile`
        content.Add(streamContent, "file", fileName);

        // --- MODIFICA QUI: Creiamo l'oggetto HttpRequestMessage ---
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);

        // Configura il contenuto (il file multipart)
        request.Content = content;

        // Aggiungi l'header personalizzato (puoi usare "OPENAI" o "GEMINI")
        // request.Headers.Add("x-model", model); 
        // request.Headers.Add("X-API-Key", "");
        // ---------------------------------------------------------


        
        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        if (!string.IsNullOrWhiteSpace(_tenant)) request.Headers.Add("x-tenant", _tenant); 
        
        // 4. Eseguiamo la chiamata POST in modo asincrono
        HttpResponseMessage response = await client.SendAsync(request);
        
        // 5. Verifichiamo l'esito
        if (response.IsSuccessStatusCode)
        {
            var ddt = await response.Content.ReadFromJsonAsync<DocResponse>().ConfigureAwait(false);
            return ddt?.DdtResponse?.Document;
        }
        else
        {
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {response.Content}");
        }
    }
}