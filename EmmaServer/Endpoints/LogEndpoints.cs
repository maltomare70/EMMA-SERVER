using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class LogEndpoints
{
    public static void MapLogsRoutes(this IEndpointRouteBuilder app)
    {

        app.MapGet("/api/logs/tenant", async (ClaimsPrincipal claims, [FromServices] ILogService logService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                string? tenant = claims.FindFirstValue("tenant");
                if ( string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                var logs = await logService.GetAllByTenantAsync(tenant);

                logs = logs.Take(1000);

                return Results.Ok(logs);
            })
            .WithName("GetLogsByTenant");
    }
}
