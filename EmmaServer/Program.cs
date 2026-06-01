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

builder.Services.AddScoped<IBolleService, BolleService>();
builder.Services.AddScoped<IBolleRepository, BolleRepository>();

builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

// 1. Registra la connessione al DB (o il tuo IUserConnectionProvider dinamico)
builder.Services.AddScoped<IDbConnection>(sp => new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Registra il validatore specifico per l'interfaccia generica
builder.Services.AddScoped<IBasicAuthValidator, JsonFileAuthValidator>();

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

app.UseAuthentication();
app.UseAuthorization();


app.MapGet("/api/v1/doc/ddt", async ([FromQuery] string fornitore, 
        [FromServices] IBolleService bolleService) =>
    {
        if (string.IsNullOrWhiteSpace(fornitore))
        {
            return Results.BadRequest("Il parametro 'fornitore' è obbligatorio.");
        }
        
        var bolle = await bolleService.GetBolleByFornitore(fornitore);

        // Gestiamo anche il caso in cui l'utente non esista
        return bolle is not null
            ? Results.Ok(bolle)
            : Results.NotFound($"Bolle del {fornitore} non trovate.");
    })
    .WithName("GetBolleByFornitore");


async Task<byte[]> ConvertFormFileToByteArray(IFormFile file)
{
    // 1. Controllo di sicurezza: verifichiamo che il file non sia vuoto
    if (file == null || file.Length == 0)
    {
        return Array.Empty<byte>(); // Oppure gestisci l'errore come preferisci
    }

    // 2. Usiamo un MemoryStream per leggere il contenuto del file
    using var memoryStream = new MemoryStream();
    
    // 3. Copiamo asincronamente lo stream del file nel MemoryStream
    await file.CopyToAsync(memoryStream);

    // 4. Trasformiamo il MemoryStream in un array di byte
    return memoryStream.ToArray();
}

/// Endpoint per l'upload del file PDF e l'inoltro a un'API esterna
app.MapPost("/api/v1/doc/ddt", async (IFormFile file, 
        [FromServices] IHttpClientFactory httpClientFactory, 
        [FromServices] IConfiguration configuration,
        [FromServices] IBolleService bolleService,
        ClaimsPrincipal user) =>
{
     if (user.Identity == null || !user.Identity.IsAuthenticated)
     {
         return Results.BadRequest("Utente non autorizzato");
     }
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

        var file_byte = await ConvertFormFileToByteArray(file);
        //// Example A: Save the file to a local directory
        //var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        //if (!Directory.Exists(uploadsFolder))
        //{
        //    Directory.CreateDirectory(uploadsFolder);
        //}

        //var filePath = Path.Combine(uploadsFolder, file.FileName);

        //using (var stream = new FileStream(filePath, FileMode.Create))
        //{
        //    await file.CopyToAsync(stream);
        //}

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
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // 3. Deserializza la stringa nell'oggetto DatiBolla
            DdtResponse? ddtResponse = JsonSerializer.Deserialize<DdtResponse>(responseContent, options);

            //Salvo sul database
            if (ddtResponse is not null)
            {
                await bolleService.AddBollaAsync(ddtResponse.Document.Mittente, 
                    ddtResponse.Document.NumeroBolla, 
                    ddtResponse.Document.DataBolla, responseContent,
                    ddtResponse.FileName ?? string.Empty, file_byte);
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
.WithName("dtt")
.DisableAntiforgery(); // FONDAMENTALE per client desktop come Avalonia

/// Aggiunge un nuovo utente
app.MapPost("/api/users", async (EmmaUser user, [FromKeyedServices] IUserService userService) =>
    {
        var id = await userService.AddUserAsync(user);
        return Results.Ok(id);
    })
    .WithName("AddUser");

/// Recupera un utente per ID
app.MapGet("/api/users/{id:int}", async (int id, [FromKeyedServices] IUserService userService) =>
    {
        var user = await userService.GetUserAsync(id);

        // Gestiamo anche il caso in cui l'utente non esista
        return user is not null
            ? Results.Ok(user)
            : Results.NotFound($"Utente con ID {id} non trovato.");
    })
    .WithName("GetUserById");

app.MapGet("/", () => "Hello");

app.Run();

