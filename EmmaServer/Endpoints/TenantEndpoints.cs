using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantRoutes(this IEndpointRouteBuilder app)
    {
        /// Crea il database e la struttura dati
        app.MapPost("/api/init", async (ClaimsPrincipal claims,
                [FromServices] IEmmaService emmaService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();
                
                await emmaService.InitAsync();
                return Results.Ok();
            })
            .WithName("Init");

        /// Aggiunge un nuovo tenant
        app.MapPost("/api/tenants",
                async (ClaimsPrincipal claims, EmmaTenant tenant, [FromServices] ITenantService tenantService) =>
                {
                    if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                    if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();

                    var id = await tenantService.AddTenantAsync(tenant);
                    return Results.Ok(id);
                })
            .WithName("AddTenant");

        /// Recupera tutti i tenants
        app.MapGet("/api/tenants", async (ClaimsPrincipal claims, [FromServices] ITenantService tenantService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();

                var tenants = await tenantService.GetAllAsync();
                return Results.Ok(tenants);
            })
            .WithName("GetTenants");


        /// Recupera un tenants per ID
        app.MapGet("/api/tenants/{id:int}", async (ClaimsPrincipal claims, int id, [FromServices] ITenantService tenantService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();
                
                var tenant = await tenantService.GetTenantAsync(id);

                // Gestiamo anche il caso in cui l'utente non esista
                return tenant is not null
                    ? Results.Ok(tenant)
                    : Results.NotFound($"tenants con ID {id} non trovato.");
            })
            .WithName("GetTenantById");

        ///Modifica di un Tenant
        app.MapPut("/api/tenants",
                async (ClaimsPrincipal user, EmmaTenant tenant, [FromServices] ITenantService tenantService) =>
                {
                    if (user.Identity == null || !user.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                    if (user.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();

                    var id = await tenantService.UpdateTenantAsync(tenant);
                    return Results.Ok(id);
                })
            .WithName("UpdateTenant");
    }
}
