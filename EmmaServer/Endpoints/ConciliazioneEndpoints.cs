using EmmaServer.Entities.Dtos;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class ConciliazioneEndpoints
{

    public static void MapConciliazioneRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/conciliazione", async ([FromQuery] string? tipo, ClaimsPrincipal claims, [FromServices] IConciliaRigheService conciliaRigheService) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

            var items = await conciliaRigheService.GetAllByTenantAsync(tenant, tipo ?? string.Empty);
            return Results.Ok(items);
        })
        .WithName("GetConciliazioneRigheByTenant");

        app.MapPost("/api/v1/conciliazione", async (
        [FromBody] InputConciliazione inputConciliazione, [FromServices] IConciliazioneService conciliazione, 
        [FromServices] IArticoliService articoliService, [FromServices] IFornitoriService fornitoreService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated)
                return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            if (!string.IsNullOrWhiteSpace(inputConciliazione.TipoConciliazione) && inputConciliazione.TipoConciliazione.Equals("ORDINI-BOLLE"))
            {
                string fornitore = inputConciliazione.Fornitore;
                var forn = await fornitoreService.GetFornitoreByCodiceAsync(fornitore, tenant);

                if (forn is not null)
                {
                    foreach (var item in inputConciliazione.Bolle)
                    {
                        var rif = await articoliService.GetRifArticoloAsync(item.Codice, forn.id);
                        if (!string.IsNullOrWhiteSpace(rif))
                        {
                            item.Codice = rif;
                        }
                    }
                }
            }
            

            var result = await conciliazione.GetConciliazioneBolleFattureAsync(inputConciliazione, tenant);
            return Results.Ok(result);
        }).WithName("Conciliazione");


        app.MapPost("/api/v1/salva-conciliazione", async (
            [FromBody] PayloadRiconciliazione payload, [FromServices] IConciliaRigheService conciliaRigheService, ClaimsPrincipal claims, [FromServices] IDocService docService) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated)
                return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            await conciliaRigheService.SalvaConcilizione(payload, tenant);


            return Results.Ok();
        }).WithName("SalvaConciliazione");



        app.MapGet("/api/v1/conciliazione/master/{idMaster}", async (ClaimsPrincipal claims, [FromRoute] string idMaster, [FromServices] IConciliaRigheService conciliaRigheService) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

            var righe = await conciliaRigheService.GetRigheConciliazioneAsync(idMaster, string.Empty, tenant);

            return Results.Ok(righe);

        }).WithName("GetRigheConciliazioneOnlyMaster");

        /// Restituisce una o più riga di conciliazione specifica per un determinato idMaster e idRiga
        app.MapGet("/api/v1/conciliazione/master/{idMaster}/riga/{idRiga}", async (ClaimsPrincipal claims, [FromRoute] string idMaster, [FromRoute] string idRiga, [FromServices] IConciliaRigheService conciliaRigheService) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

            var righe = await conciliaRigheService.GetRigheConciliazioneAsync(idMaster, idRiga, tenant);

            return Results.Ok(righe);

        }).WithName("GetRigheConciliazione");

    }
}
