using System.Security.Claims;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace EmmaServer.Endpoints;

/// <summary>
/// Rotte del modulo anomalie (docs/modulo-anomalie.md, par. 7): lista, feedback e statistiche,
/// piu' il ricalcolo della baseline e l'analisi del singolo documento.
/// </summary>
public static class AnomalieEndpoints
{
    public static void MapAnomalieRoutes(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------------
        // Lista, feedback, statistiche (docs/modulo-anomalie.md, par. 7)
        // ------------------------------------------------------------------

        // Lista del tenant. severita = severita' MINIMA (default 1: gli avvisi in ombra non si vedono).
        // Esempio: /api/v1/anomalie?stato=0&severita=2&fornitore=12&da=2026-09-01&a=2026-09-15&limit=50
        app.MapGet("/api/v1/anomalie", async (ClaimsPrincipal claims,
                [FromServices] IAnomalieService anomalieService,
                [FromQuery] short? stato, [FromQuery] short? severita, [FromQuery] int? fornitore,
                [FromQuery] DateTime? da, [FromQuery] DateTime? a,
                [FromQuery] short? livello, [FromQuery] string? tipo, [FromQuery] int? docId,
                [FromQuery] int? limit, [FromQuery] int? offset,
                CancellationToken ct) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                var tenant = claims.FindFirstValue("tenant");
                if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                var filtri = new FiltriAnomalie
                {
                    Stato = stato,
                    Severita = severita,
                    Fornitore = fornitore,
                    Da = da,
                    A = a,
                    Livello = livello,
                    Tipo = tipo,
                    DocId = docId,
                    Limit = limit ?? 100,
                    Offset = offset ?? 0,
                };

                try
                {
                    return Results.Ok(await anomalieService.GetAnomalieAsync(tenant, filtri, ct));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            })
            .WithName("GetAnomalie");

        // Feedback dell'operatore: { "stato": 1 | 2, "note": "..." }. stato 0 riapre l'avviso.
        // E' la raccolta delle etichette del futuro filtro AutoML.
        app.MapPost("/api/v1/anomalie/{id:long}/feedback", async (long id, [FromBody] FeedbackAnomalia feedback,
                ClaimsPrincipal claims, [FromServices] IAnomalieService anomalieService, CancellationToken ct) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                var tenant = claims.FindFirstValue("tenant");
                if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                try
                {
                    var anomalia = await anomalieService.RegistraFeedbackAsync(tenant, id, feedback, claims.Identity.Name, ct);
                    return anomalia is null
                        ? Results.NotFound($"Anomalia {id} non trovata.")
                        : Results.Ok(anomalia);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            })
            .WithName("FeedbackAnomalia");

        // Precisione degli avvisi mostrati (confermate / (confermate + ignorate)), per tipo e complessiva.
        app.MapGet("/api/v1/anomalie/statistiche", async (ClaimsPrincipal claims,
                [FromServices] IAnomalieService anomalieService,
                [FromQuery] DateTime? da, [FromQuery] DateTime? a, [FromQuery] short? livello,
                CancellationToken ct) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                var tenant = claims.FindFirstValue("tenant");
                if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                if (da is { } dal && a is { } al && al < dal) return Results.BadRequest("'a' precede 'da'.");

                return Results.Ok(await anomalieService.GetStatisticheAsync(tenant, da, a, livello, ct));
            })
            .WithName("GetStatisticheAnomalie");

        // Ricalcolo manuale della baseline: l'admin la ricalcola per tutti i tenant,
        // un utente normale solo per il proprio.
        app.MapPost("/api/v1/anomalie/baseline", async (ClaimsPrincipal claims,
                [FromServices] IAnomalieService anomalieService, CancellationToken ct) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                if (claims.Identity.Name?.ToLower() == EmmaAdmin.ADMIN)
                    return Results.Ok(await anomalieService.RicalcolaTutteLeBaselineAsync(ct));

                var tenant = claims.FindFirstValue("tenant");
                if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                return Results.Ok(new[] { await anomalieService.RicalcolaBaselineAsync(tenant, ct) });
            })
            .WithName("RicalcolaBaselineAnomalie");

        // Avvisi di un documento, per il badge nella pagina di dettaglio.
        app.MapGet("/api/v1/anomalie/doc/{docId:int}", async (int docId, ClaimsPrincipal claims,
                [FromServices] IAnomalieService anomalieService, CancellationToken ct) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                var tenant = claims.FindFirstValue("tenant");
                if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                return Results.Ok(await anomalieService.GetAnomalieDocumentoAsync(tenant, docId, ct));
            })
            .WithName("GetAnomalieDocumento");

        // Rilancia il livello 2 su un documento gia' importato (es. dopo aver ricalcolato la baseline).
        app.MapPost("/api/v1/anomalie/doc/{docId:int}/analizza", async (int docId, ClaimsPrincipal claims,
                [FromServices] IAnomalieService anomalieService, CancellationToken ct) =>
            {
                if (claims.Identity == null || !claims.Identity.IsAuthenticated) return Results.BadRequest("Utente non autorizzato");

                var tenant = claims.FindFirstValue("tenant");
                if (string.IsNullOrWhiteSpace(tenant)) return Results.BadRequest("Tenant non presente.");

                var avvisi = await anomalieService.AnalizzaDocumentoAsync(docId, tenant, ct);
                return avvisi is null
                    ? Results.NotFound($"Documento {docId} non trovato.")
                    : Results.Ok(avvisi);
            })
            .WithName("AnalizzaAnomalieDocumento");
    }
}
