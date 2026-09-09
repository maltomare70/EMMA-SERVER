using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;


namespace EmmaServer.Endpoints;



public static class DocEndpoints
{
    // Questo metodo accetta l'app e registra le rotte al suo interno
    public static void MapDocRoutes(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/doc/clean", async (
          [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            await docService.CleanDocAsync();
            return Results.Ok();
        }).WithName("CleanDocs");

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

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            await docService.DeleteRigaDocAsync(articoloBolla, tenant);
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

        //Per il camboio stato
        app.MapPost("/api/v1/doc/tipo", async (CambioTipo cambioTipo, [FromServices] IDocService docService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            await docService.CambiaTipoAsync(cambioTipo);
            return Results.Ok();

        }).WithName("CambiaTipo");

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
        

        /// Endpoint per l'upload del file PDF e l'inoltro a un'API esterna ORDINI BOLLE FATTURE
        app.MapPost("/api/v1/doc", async (IFormFile file, 
                [FromServices] IDocService docService,
                ClaimsPrincipal claims) =>
        {
             if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
             
            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            // 1. Validate that a file was actually uploaded
            if (file.Length == 0) return Results.BadRequest("No file was uploaded.");

            // 2. Validate that the file is a PDF
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            try
            {
                if (extension == ".pdf") return Results.Ok(await docService.ImportDocAsync(file, tenant));
                else if (extension== ".xml") return Results.Ok(await docService.ImportFatturaElettronicaAsync(file, tenant));
                else return Results.BadRequest($"{extension} files not allowed.");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Internal server error: {ex.Message}");
            }

        })
        .WithName("doc")
        .DisableAntiforgery(); // FONDAMENTALE per client desktop come Avalonia


        // =====================================================================
        //  DATABASE VETTORIALE
        //  PDF -> testo (PdfPig) -> chunk a finestra fissa con sovrapposizione
        //      (Microsoft.ML.Tokenizers) -> embedding (EMMA-AI) -> pgvector
        // =====================================================================

        /// Acquisisce un PDF, lo spezza in chunk e lo vettorizza su rag_documents / rag_chunks
        app.MapPost("/api/v1/document", async (IFormFile file,
                [FromServices] IRagService ragService,
                ClaimsPrincipal claims,
                CancellationToken ct) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            if (file.Length == 0) return Results.BadRequest("No file was uploaded.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".pdf") return Results.BadRequest($"{extension} files not allowed.");

            try
            {
                var risposta = await ragService.IndicizzaPdfAsync(file, tenant, ct);
                return Results.Ok(risposta);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Internal server error: {ex.Message}");
            }

        })
        .WithName("document")
        .DisableAntiforgery(); // FONDAMENTALE per client desktop come Avalonia


        /// Ricerca semantica sui documenti vettorizzati del tenant
        app.MapPost("/api/v1/document/search", async (
                [FromBody] RagSearchRequest richiesta,
                [FromServices] IRagService ragService,
                ClaimsPrincipal claims,
                CancellationToken ct) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            try
            {
                var risultati = await ragService.CercaAsync(richiesta, tenant, ct);
                return Results.Ok(risultati);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Internal server error: {ex.Message}");
            }

        })
        .WithName("documentSearch")
        .DisableAntiforgery();


        /// Elenco dei documenti indicizzati (senza il PDF)
        app.MapGet("/api/v1/document", async (
                [FromServices] IRagService ragService,
                ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            return Results.Ok(await ragService.GetDocumentiAsync(tenant));

        })
        .WithName("documentList");


        /// Restituisce il PDF originale di un documento indicizzato
        app.MapGet("/api/v1/document/{id:int}/file", async (int id,
                [FromServices] IRagService ragService,
                ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            var doc = await ragService.GetAllegatoAsync(id, tenant);
            if (doc?.allegato is null || doc.allegato.Length == 0)
                return Results.NotFound($"Nessun allegato per il documento {id}.");

            return Results.File(doc.allegato, "application/pdf", doc.file_name);

        })
        .WithName("documentFile");


        /// Diagnostica: cosa e' finito davvero nel database vettoriale per questo documento
        app.MapGet("/api/v1/document/{id:int}/chunks", async (int id,
                [FromServices] IRagService ragService,
                ClaimsPrincipal claims,
                int? skip, int? take) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            var chunks = await ragService.GetChunksAsync(id, tenant, skip ?? 0, take ?? 10);
            return Results.Ok(chunks);

        })
        .WithName("documentChunks");


        /// Cancella un documento indicizzato e, in cascata, i suoi chunk
        app.MapDelete("/api/v1/document/{id:int}", async (int id,
                [FromServices] IRagService ragService,
                ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            return await ragService.EliminaDocumentoAsync(id, tenant)
                ? Results.Ok()
                : Results.NotFound($"Documento {id} non trovato.");

        })
        .WithName("documentDelete");
    }

}
