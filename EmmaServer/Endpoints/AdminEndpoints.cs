
using EmmaServer.Helpers;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using EmmaServer.Entities;

namespace EmmaServer.Endpoints;



public static class AdminEndpoints
{
    public static void MapAdminRoutes(this IEndpointRouteBuilder app)
    {
        /// Crea il database e la struttura dati
        app.MapPost("/api/database/init", async (ClaimsPrincipal claims,
                [FromServices] IEmmaService emmaService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();
                
                await emmaService.InitAsync();
                return Results.Ok();
            })
            .WithName("Init");
        
        app.MapPost("/api/password", (PasswordRequest request) =>
            {
                // Accediamo alla proprietà del Record
                var hash = PasswordHelper.GeneraHash(request.Password);
                return Results.Ok(hash);
            })
            .WithName("createPassword");
        
        app.MapPost("/api/database/test", async (ClaimsPrincipal claims,
                [FromServices] IEmmaService emmaService) =>
            {
                await emmaService.TestAsync();
                return Results.Ok();
            })
            .WithName("test");
        
    }
}