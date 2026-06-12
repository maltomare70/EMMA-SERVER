using EmmaServer;
using EmmaServer.Entities;
using EmmaServer.Repositories;
using EmmaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Claims;
using Dapper;

var builder = WebApplication.CreateBuilder(args);


SqlMapper.AddTypeHandler(new JsonDocumentTypeHandler());

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpContextAccessor();

// 2. Registriamo il provider della connessione dinamica
builder.Services.AddScoped<IUserConnectionProvider, UserConnectionProvider>();
builder.Services.AddScoped(typeof(IRepositoryGenerico<>), typeof(RepositoryGenerico<>));
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<ITenantService, TenantService>();

builder.Services.AddScoped<IBolleService, BolleService>();
builder.Services.AddScoped<IBolleRepository, BolleRepository>();

builder.Services.AddScoped<IBolleMasterRepository, BolleMasterRepository>();
builder.Services.AddScoped<IBolleMasterService, BolleMasterService>();

builder.Services.AddScoped<IBolleRowsRepository, BolleRowsRepository>();

builder.Services.AddScoped<IDocRepository, DocRepository>();
builder.Services.AddScoped<IDocService, DocService>();


builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

// 1. Registra la connessione al DB (o il tuo IUserConnectionProvider dinamico)
builder.Services.AddScoped<IDbConnection>(sp => new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Registra il validatore specifico per l'interfaccia generica
builder.Services.AddScoped<IBasicAuthValidator, DatabaseAuthValidator>();

// 3. Registra l'autenticazione Basic (che troverà automaticamente IBasicAuthValidator)
builder.Services.AddAuthentication("BasicAuthentication")
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

builder.Services.AddAuthorization();

builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// Per acquisire tutte le bolle di un fornitore
// richiede filtri aggiuntivi per essere utilizzata
// tutte le aperte... ad una certa data ...
app.MapGet("/api/v1/doc", async ([FromQuery] string fornitore, 
        [FromServices] IDocService docService) =>
    {
        if (string.IsNullOrWhiteSpace(fornitore))
        {
            return Results.BadRequest("Il parametro 'fornitore' è obbligatorio.");
        }
        
        var docs = await docService.GetDocByFornitore(fornitore);
        
        return docs is not null
            ? Results.Ok(docs)
            : Results.NotFound($"Doc del {fornitore} non trovate.");
    })
    .WithName("GetDocByFornitore");



/// Endpoint per l'upload del file PDF e l'inoltro a un'API esterna
app.MapPost("/api/v1/doc", async (IFormFile file, 
        [FromServices] IHttpClientFactory httpClientFactory, 
        [FromServices] IConfiguration configuration,
        [FromServices] IDocService docService,
        ClaimsPrincipal user) =>
{
     if (user.Identity == null || !user.Identity.IsAuthenticated)
     {
         return Results.BadRequest("Utente non autorizzato");
     }

     string tenant = user.FindFirstValue("tenant");
     
    // 1. Validate that a file was actually uploaded
    if (file.Length == 0) return Results.BadRequest("No file was uploaded.");

    // 2. Validate that the file is a PDF
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (extension != ".pdf")
    {
        return Results.BadRequest("Only PDF files are allowed.");
    }
    try
    {
        var file_byte = await FileHelper.ConvertFormFileToByteArray(file);
        
        //Access the file stream directly (e.g., to upload to AWS S3, Azure Blob, or database)
        using var stream = file.OpenReadStream();

        // 2. Create the HttpClient instance
        var client = httpClientFactory.CreateClient();

        // 3. Prepare the multipart form data content
        using var form = new MultipartFormDataContent();

        // Open the stream of the incoming file
        using var fileStream = file.OpenReadStream();
        using var streamContent = new StreamContent(fileStream);

        // Pass along the original Content-Type headers
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

        // "file" is the parameter name the external API expects. 
        // file.FileName ensures the external API knows the original file name.
        form.Add(streamContent, "file", file.FileName);
        
        // 4. Send POST request to the external/internal API
        var url = configuration["EMMA-AI:EndPoint"]; //https://emma-aegc.onrender.com",
        var externalApiUrl = $"{url}/api/v1/doc/ddt";
        
        using var request = new HttpRequestMessage(HttpMethod.Post, externalApiUrl);
        request.Content = form;

        // ADD YOUR HEADERS HERE
        var model = configuration["EMMA-AI:Model"];
        request.Headers.Add("x-model", model); 
        var apiKey = configuration["EMMA-AI:ApiKey"];
        request.Headers.Add("X-API-Key", apiKey);
        
        //var response = await client.PostAsync(externalApiUrl, form);
        var response = await client.SendAsync(request);
        
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            // 3. Deserializza la stringa nell'oggetto DatiBolla
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            DdtResponse? ddtResponse = JsonSerializer.Deserialize<DdtResponse>(responseContent, options);

            //Salvo sul database
            if (ddtResponse is not null)
            {
                int? id = await docService.AddDocAsync(ddtResponse.Document.Mittente, 
                    ddtResponse.Document.NumeroBolla, 
                    ddtResponse.Document.DataBolla, responseContent,
                    ddtResponse.FileName ?? string.Empty, file_byte, tenant);
            }

            return Results.Ok(ddtResponse);
        }
        else
        {
            return Results.Problem($"Internal server error: {response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        // Log the exception in real production code
        return Results.Problem($"Internal server error: {ex.Message}");
    }
})
.WithName("doc")
.DisableAntiforgery(); // FONDAMENTALE per client desktop come Avalonia


// //Aggiunge le righe del DDt nell tabella per la conciliazione
// app.MapPost("/api/v1/doc/ddt/add-rows", async ([FromBody] DatiBolla dati_bolla, 
//         [FromServices] IBolleMasterService bolleMasterService, 
//         [FromServices] IBolleService bolleService) =>
//     {
//         var bolla = await bolleService.GetBollaAsync(dati_bolla.Mittente, 
//             dati_bolla.NumeroBolla,  
//             dati_bolla.DataBolla);
//         await bolleMasterService.AddAsync(bolla.id, dati_bolla);
//         return Results.Ok();
//     })
//     .WithName("AddDdtRows");

// //Delete Master/Rrows
// app.MapDelete ("/api/v1/doc/ddt", async([FromBody] BolleMaster bolleMaster,  
//         [FromServices] IBolleMasterService bolleMasterService) =>
// {
//     await bolleMasterService.DeleteAsync(bolleMaster);
//     return Results.Ok();
// })
// .WithName("DeleteDdt");


/// Aggiunge un nuovo tenant
app.MapPost("/api/tenants", async (EmmaTenant tenant, [FromServices] ITenantService tenantService) =>
    {
        var id = await tenantService.AddTenantAsync(tenant);
        return Results.Ok(id);
    })
    .WithName("AddTenant");

app.MapGet("/api/tenants", async ([FromServices] ITenantService tenantService) =>
    {
        var tenants = await tenantService.GetAllAsync();
        return Results.Ok(tenants);
    })
    .WithName("GetTenants");


/// Recupera un tenants per ID
app.MapGet("/api/tenants/{id:int}", async (int id, [FromServices] ITenantService tenantService) =>
    {
        var tenant = await tenantService.GetTenantAsync(id);

        // Gestiamo anche il caso in cui l'utente non esista
        return tenant is not null
            ? Results.Ok(tenant)
            : Results.NotFound($"tenants con ID {id} non trovato.");
    })
    .WithName("GetTenantById");

app.MapPut("/api/tenants", async (EmmaTenant tenant, [FromServices] ITenantService tenantService) =>
    {
        var id = await tenantService.UpdateTenantAsync(tenant);
        return Results.Ok(id);
    })
    .WithName("UpdateTenant");

/// Aggiunge un nuovo utente
app.MapPost("/api/users", async (EmmaUser user, [FromServices] IUserService userService) =>
    {
        var id = await userService.AddUserAsync(user);
        return Results.Ok(id);
    })
    .WithName("AddUser");

app.MapPut("/api/users", async (EmmaUser user, [FromServices] IUserService userService) =>
    {
        var id = await userService.UpdateUserAsync(user);
        return Results.Ok(id);
    })
    .WithName("UpdateUser");

app.MapGet("/api/users", async ([FromServices] IUserService userService) =>
    {
        var users = await userService.GetAllTenantAsync();
        return Results.Ok(users);
    })
    .WithName("GetUsers");

/// Recupera un utente per ID
app.MapGet("/api/users/{id:int}", async (int id, [FromServices] IUserService userService) =>
    {
        var user = await userService.GetUserAsync(id);

        // Gestiamo anche il caso in cui l'utente non esista
        return user is not null
            ? Results.Ok(user)
            : Results.NotFound($"Utente con ID {id} non trovato.");
    })
    .WithName("GetUserById");

app.MapGet("/api/users/email/{email}", async (string email, [FromServices] IUserService userService) =>
    {
        var user = await userService.GetUserByEmailAsync(email);

        // Gestiamo anche il caso in cui l'utente non esista
        return user is not null
            ? Results.Ok(user)
            : Results.NotFound($"Utente con email {email} non trovato.");
    })
    .WithName("GetUserByEmail");

//Test
app.MapGet("/", () => "Hello");

//Auth
app.MapGet("/api/auth", () => "OK");

app.Run();

