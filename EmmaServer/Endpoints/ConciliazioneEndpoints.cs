
using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class ConciliazioneEndpoints
{

    public static void MapConciliazioneRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/conciliazione", async (ClaimsPrincipal claims, [FromServices] IConciliaRigheService conciliaRigheService) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

            var logs = await conciliaRigheService.GetAllByTenantAsync(tenant);

            return Results.Ok(logs);
        })
        .WithName("GetConciliazioneRigheByTenant");

        app.MapPost("/api/v1/conciliazione", async (
        [FromBody] InputConciliazione inputConciliazione, [FromServices] IConciliazioneService conciliazione, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated)
                return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            var result = await conciliazione.GetConciliazioneBolleFattureAsync(inputConciliazione, tenant);
            return Results.Ok(result);
        }).WithName("Conciliazione");


        app.MapPost("/api/v1/salva-conciliazione", async (
            [FromBody] PayloadRiconciliazione payload, [FromServices] IConciliaRigheService conciliaRigheService, ClaimsPrincipal claims) =>
        {
            if (claims.Identity == null || !claims.Identity.IsAuthenticated)
                return Results.BadRequest("Utente non autorizzato");

            string? tenant = claims.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("No tenant.");

            foreach (var b in payload.bolle)
            {
                var item = new EmmaConciliaRighe()
                {
                    codice = payload.codice ?? string.Empty,
                    delta = b.Qta - b.Qta_Conc,
                    flag = b.Selezionato ? 1 : 0,
                    id_fornitore = b.Fornitore ?? string.Empty,
                    id_master = b.IdMaster ?? string.Empty,
                    id_riga = b.IdRiga ?? string.Empty,
                    note = b.Note ?? string.Empty,
                    qta = b.Qta,
                    stato = b.Stato ?? string.Empty,
                    tipo_doc = 3,
                    qta_canc = b.Qta_Conc,
                    tenant = tenant
                };

                await conciliaRigheService.DeleteAsync(item.id_riga, tenant);

                await conciliaRigheService.AddAsync(item);
            }


            foreach (var b in payload.fatture)
            {
                var item = new EmmaConciliaRighe()
                {
                    codice = payload.codice ?? string.Empty,
                    delta = b.Qta - b.Qta_Conc,
                    flag = b.Selezionato ? 1 : 0,
                    id_fornitore = b.Fornitore ?? string.Empty,
                    id_master = b.IdMaster ?? string.Empty,
                    id_riga = b.IdRiga ?? string.Empty,
                    note = b.Note ?? string.Empty,
                    qta = b.Qta,
                    stato = b.Stato ?? string.Empty,
                    tipo_doc = 3,
                    qta_canc = b.Qta_Conc,
                    tenant = tenant
                };

                await conciliaRigheService.DeleteAsync(item.id_riga, tenant);

                await conciliaRigheService.AddAsync(item);
            }

            return Results.Ok();
        }).WithName("SalvaConciliazione");
    }

    //public static void MapConciliazioneRoutes(this IEndpointRouteBuilder app)
    //{
    //    app.MapPost("/api/v1/conciliazione", async (
    //    [FromBody] PayloadRiconciliazione payload, [FromServices] IConciliazioneService conciliazione, ClaimsPrincipal claims) =>
    //    {
    //        if (claims.Identity == null || !claims.Identity.IsAuthenticated)
    //            return Results.BadRequest("Utente non autorizzato");

    //        var result = await conciliazione.GetConciliazione(payload.bolle, payload.fatture);
    //        return Results.Ok(result);
    //    }).WithName("Conciliazione");
    //}


}
