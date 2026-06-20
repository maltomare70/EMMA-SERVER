using EmmaServer.Entities;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EmmaServer.Endpoints;

public static class UserEndpoints
{
    // Questo metodo accetta l'app e registra le rotte al suo interno
    public static void MapUserRoutes(this IEndpointRouteBuilder app)
    {
        /// Aggiunge un nuovo utente
        app.MapPost("/api/users", async (ClaimsPrincipal claims, EmmaUser user, [FromServices] IUserService userService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();

                var id = await userService.AddUserAsync(user);
                return Results.Ok(id);
            })
            .WithName("AddUser");

        ///Modifica un utente
        app.MapPut("/api/users", async (ClaimsPrincipal claims, EmmaUser user, [FromServices] IUserService userService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();
                
                var id = await userService.UpdateUserAsync(user);
                return Results.Ok(id);
            })
            .WithName("UpdateUser");

        //Recupera tutti gli utenti
        app.MapGet("/api/users", async (ClaimsPrincipal claims, string tenant, HttpContext httpContext, [FromServices] IUserService userService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();

                var users = await userService.GetAllTenantAsync(tenant);
                return Results.Ok(users);
            })
            .WithName("GetUsers");

        /// Recupera un utente per ID
        app.MapGet("/api/users/{id:int}", async (ClaimsPrincipal claims, int id, [FromServices] IUserService userService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();
                
                var user = await userService.GetUserAsync(id);

                // Gestiamo anche il caso in cui l'utente non esista
                return user is not null
                    ? Results.Ok(user)
                    : Results.NotFound($"Utente con ID {id} non trovato.");
            })
            .WithName("GetUserById");

        //Recupera un utente dall'email
        app.MapGet("/api/users/email/{email}", async (ClaimsPrincipal claims, string email, [FromServices] IUserService userService) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");
                if (claims.Identity?.Name?.ToLower() != "admin") return Results.Unauthorized();
                
                var user = await userService.GetUserByEmailAsync(email);

                // Gestiamo anche il caso in cui l'utente non esista
                return user is not null
                    ? Results.Ok(user)
                    : Results.NotFound($"Utente con email {email} non trovato.");
            })
            .WithName("GetUserByEmail");
    }
}
