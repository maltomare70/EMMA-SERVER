using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Claims;

namespace EmmaServer.Endpoints;


public static class DocEndpoints
{
    // Questo metodo accetta l'app e registra le rotte al suo interno
    public static void MapDocRoutes(this IEndpointRouteBuilder app)
    {
        //una volta salvato il documento
        //si caricano le anagrafiche Forniori e Articoli
        app.MapPost("/api/v1/doc/anagrafiche", async (
            [FromBody] int idDoc, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            await docService.AddOrUpdateFornitorieArticoli(idDoc);
            Results.Ok();
        } ).WithName("Anagrafiche");
        
        // Per acquisire tutte le bolle di un fornitore
        // richiede filtri aggiuntivi per essere utilizzata
        // tutte le aperte... ad una certa data ...
        app.MapGet("/api/v1/doc", async ([FromQuery] string fornitore, 
                [FromServices] IDocService docService, ClaimsPrincipal claims) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (string.IsNullOrWhiteSpace(fornitore)) return Results.BadRequest("Il parametro 'fornitore' è obbligatorio.");
                
                var docs = await docService.GetDocByFornitore(fornitore);
                
                return docs is not null
                    ? Results.Ok(docs)
                    : Results.NotFound($"Doc del {fornitore} non trovate.");
            })
            .WithName("GetDocByFornitore");
        
        
        /// Endpoint per l'upload del file PDF e l'inoltro a un'API esterna
        app.MapPost("/api/v1/doc", async (IFormFile file, 
                [FromServices] IHttpClientFactory httpClientFactory, 
                [FromServices] IConfiguration configuration,
                [FromServices] IDocService docService,
                ClaimsPrincipal claims) =>
        {
             if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
             
             string tenant = claims.FindFirstValue("tenant");
             
            // 1. Validate that a file was actually uploaded
            if (file.Length == 0) return Results.BadRequest("No file was uploaded.");

            // 2. Validate that the file is a PDF
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".pdf") return Results.BadRequest("Only PDF files are allowed.");
            
            try
            {
                var file_byte = await FileHelper.ConvertFormFileToByteArray(file);
                
                //Access the file stream directly (e.g., to upload to AWS S3, Azure Blob, or database)
                using var stream = file.OpenReadStream();

                // 2. Create the HttpClient instance
                var client = httpClientFactory.CreateClient();

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
                var url = configuration["EMMA-AI:EndPoint"]; //https://emma-aegc.onrender.com",
                var externalApiUrl = $"{url}/api/v1/doc/ddt";
                
                using var request = new HttpRequestMessage(HttpMethod.Post, externalApiUrl);
                request.Content = form;

                // ADD YOUR HEADERS HERE
                var model = configuration["EMMA-AI:Model"];
                request.Headers.Add("x-model", model); 
                var apiKey = configuration["EMMA-AI:ApiKey"];
                request.Headers.Add("X-API-Key", apiKey);
                
                //var response = await client.PostAsync(externalApiUrl, form);
                var response = await client.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    // 3. Deserializza la stringa nell'oggetto DatiBolla
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    DdtResponse? ddtResponse = JsonSerializer.Deserialize<DdtResponse>(responseContent, options);

                    //Salvo sul database
                    int? idDoc = 0;
                    if (ddtResponse is not null)
                    {
                        idDoc = await docService.AddDocAsync(ddtResponse.Document.Mittente, 
                            ddtResponse.Document.NumeroBolla, 
                            ddtResponse.Document.DataBolla, responseContent,
                            ddtResponse.FileName ?? string.Empty, file_byte, tenant);
                    }
                    
                    //--------------------------------------------------------
                    //Aggiorna Anagrafiche
                    //--------------------------------------------------------
                   await AggiornaAnagrafiche(docService,  idDoc.Value);
                   //--------------------------------------------------------
                   
                    return Results.Ok(new DocResponse()
                    {
                        DocId = idDoc.Value,
                        DdtResponse =  ddtResponse,
                    });
                }
                else
                {
                    return Results.Problem($"Internal server error: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // Log the exception in real production code
                return Results.Problem($"Internal server error: {ex.Message}");
            }
        })
        .WithName("doc")
        .DisableAntiforgery(); // FONDAMENTALE per client desktop come Avalonia
    }

    static private async Task AggiornaAnagrafiche(IDocService docService,int idDoc)
    {
        try
        {
            await docService.AddOrUpdateFornitorieArticoli(idDoc);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}

