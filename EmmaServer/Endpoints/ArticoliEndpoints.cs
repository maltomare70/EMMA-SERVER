using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Claims;


namespace EmmaServer.Endpoints;

public static class ArticoliEndpoints
{
    public static void MapArticoliRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/articoli", async (
                [FromServices] IArticoliService articoliService, [FromQuery] string fornitore, ClaimsPrincipal claims) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                
                var articoli = await articoliService.GetAllTenantAsync(fornitore);
                
                return articoli is not null
                    ? Results.Ok(articoli)
                    : Results.NotFound($"Righe non trovate.");
            })
            .WithName("GetArticoli");
        
        app.MapPost("/api/articoli", async (ClaimsPrincipal claims, EmmaArticoli articolo, [FromServices] IArticoliService articoliService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");


                var id = await articoliService.AddArticoloAsync(articolo);
                return Results.Ok(id);
            })
            .WithName("AddArticolo");
        
        
        app.MapPut("/api/articoli", async (ClaimsPrincipal claims, EmmaArticoli articolo, [FromServices] IArticoliService articoliService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");


                var id = await articoliService.UpdateArticoloAsync(articolo);
                return Results.Ok(id);
            })
            .WithName("UpdateArticolo");
        
        app.MapDelete("/api/articoli", async (ClaimsPrincipal claims, [FromBody] EmmaArticoli articolo, [FromServices] IArticoliService articoliService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");


                var id = await articoliService.DeleteArticoloAsync(articolo);
                return Results.Ok(id);
            })
            .WithName("DeleteArticolo");
    }
}