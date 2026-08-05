
using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class ConciliazioneEndpoints
{
    public static void MapConciliazioneRoutes(this IEndpointRouteBuilder app)
    {
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
