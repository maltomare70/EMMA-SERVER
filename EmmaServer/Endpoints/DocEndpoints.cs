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
        //si allineano le anagrafiche Forniori e Articoli
        //questa api è per test
        app.MapPost("/api/v1/doc/anagrafiche", async (
            [FromBody] int idDoc, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) 
                return Results.BadRequest("Utente non autorizzato");
            
            await docService.AddOrUpdateFornitorieArticoli(idDoc);
            return Results.Ok();
        } ).WithName("AllineamentoAnagrafiche");
        
        //aggiunta di nuova riga
        app.MapPost("/api/v1/doc/riga", async (
            [FromBody] ArticoloBolla articoloBolla, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
            
            await docService.InsertRigaDocAsync(articoloBolla);
            return Results.Ok();
        } ).WithName("AggiungiRigaDoc");
        
        //Modifica riga esistente
        app.MapPut("/api/v1/doc/riga", async (
            [FromBody] ArticoloBolla articoloBolla, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
            
            await docService.UpdateRigaDocAsync(articoloBolla);
            return Results.Ok();
        } ).WithName("ModificaRigaDoc");
        
        //cancellazione riga
        app.MapDelete("/api/v1/doc/riga", async (
            [FromBody] ArticoloBolla articoloBolla, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
            
            await docService.DeleteRigaDocAsync(articoloBolla);
            return Results.Ok();
        } ).WithName("CancellazioneRigaDoc");
        
        //cancellazione intero documento
        app.MapDelete("/api/v1/doc", async (
            [FromBody] EmmaDocFilters docFilters, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
            
            await docService.DeleteDocAsync(docFilters);
            return Results.Ok();
        } ).WithName("CancellazioneDoc");
        
        // Per acquisire tutte i documenti secondo quanto filtrato
        app.MapPost("/api/v1/doc", async (EmmaDocFilters docFilters,
                [FromServices] IDocService docService, ClaimsPrincipal claims) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                
                var docs = await docService.GetDocsAsync(docFilters);
                
                return docs is not null
                    ? Results.Ok(docs)
                    : Results.NotFound($"Doc del {docFilters.Fornitore} non trovate.");
            })
            .WithName("GetDocs");

        //Per il camboio stato
        app.MapPost ("/api/v1/doc/stato", async (CambioStato cambioStato, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            await docService.CambiaStatoAsync(cambioStato);
            return Results.Ok();
            
        }).WithName("CambiaStato");
        
        
        ///to ping AI Service
        app.MapGet("/api/health", async (
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] IConfiguration configuration) =>
        {
            var url = configuration["EMMA-AI:EndPoint"]; 
            var externalApiUrl = $"{url}/api/health"; 
    
            using var request = new HttpRequestMessage(HttpMethod.Get, externalApiUrl);
    
            // Create the client configured with the retry policy
            var client = httpClientFactory.CreateClient("RenderService");
    
            try
            {
                var response = await client.SendAsync(request);
        
                if (response.IsSuccessStatusCode)
                {
                    return Results.Ok(new { status = "Healthy", externalApi = "Online" });
                }
        
                return Results.StatusCode((int)response.StatusCode);
            }
            catch (HttpRequestException)
            {
                // Fires if all retries fail because the external server didn't wake up in time
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("health")
        .DisableAntiforgery(); // FONDAMENTALE per client desktop come Avalonia
        
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
                        EmmaDocFilters emmaDocFilters = new EmmaDocFilters()
                        {
                                Fornitore = ddtResponse.Document.Mittente,
                                NumeroDoc = ddtResponse.Document.NumeroBolla,
                                DataDoc = ddtResponse.Document.DataBolla,
                                Stato = -1,
                                TipoDoc = int.Parse(ddtResponse.Document.TipoDocumento)
                        };
                        responseContent =  JsonSerializer.Serialize(ddtResponse, options);
                        
                        idDoc = await docService.AddDocAsync(emmaDocFilters , 
                             responseContent,
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

     private static async Task AggiornaAnagrafiche(IDocService docService,int idDoc)
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

