using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class  FornitoreEndpoints
{
    public static void MapFornitoreRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/fornitori/doc/{docId:int}", async ( [FromRoute] int docId, ClaimsPrincipal claims, [FromServices] IFornitoriService fornitoriService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                await fornitoriService.AddOrUpdateFornitoriByDocIdAsync(docId);
                return Results.Ok();
            })
            .WithName("AddOrUpdateFornitoriByDocId");
        
        app.MapPost("/api/fornitori", async (ClaimsPrincipal claims, EmmaFornitori fornitore, [FromServices] IFornitoriService fornitoriService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");


                var id = await fornitoriService.AddFornitoreAsync(fornitore);
                return Results.Ok(id);
            })
            .WithName("AddFornitore");
        
        app.MapPut("/api/fornitori", async (ClaimsPrincipal claims, EmmaFornitori fornitore, [FromServices] IFornitoriService fornitoriService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");


                var id = await fornitoriService.UpdateFornitoreAsync(fornitore);
                return Results.Ok(id);
            })
            .WithName("UpdateFornitore");
        
        app.MapGet("/api/fornitori", async (ClaimsPrincipal claims, string tenant, HttpContext httpContext, [FromServices] IFornitoriService fornitoriService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");


                var fornitori = await fornitoriService.GetAllTenantAsync();
                return Results.Ok(fornitori);
            })
            .WithName("GetFornitori");
        
        app.MapGet("/api/fornitori/{id:int}", async (ClaimsPrincipal claims, int id, [FromServices] IFornitoriService fornitoriService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                
                var fornitore = await fornitoriService.GetFornitoreAsync(id);

                // Gestiamo anche il caso in cui l'utente non esista
                return fornitore is not null
                    ? Results.Ok(fornitore)
                    : Results.NotFound($"Fornitore con ID {id} non trovato.");
            })
            .WithName("GetFornitoreById");
    }
}