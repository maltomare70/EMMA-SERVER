using Dapper;
using Emma.Batches;
using EmmaServer;
using EmmaServer.Background;
using EmmaServer.Endpoints;
using EmmaServer.Entities;
using EmmaServer.Repositories;
using EmmaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Npgsql;
using Scalar.AspNetCore;
using System.Data;
using System.Security.Claims;

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
builder.Services.AddScoped<IEmmaService, EmmaService>();
builder.Services.AddScoped<IEmmaRepository, EmmaRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IFornitoriRepository, FornitoriRepository>();
builder.Services.AddScoped<IFornitoriService, FornitoriService>();
builder.Services.AddScoped<IArticoliService, ArticoliService>();
builder.Services.AddScoped<IArticoliRepository, ArticoliRepository>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ILogRepository, LogRepository>();
builder.Services.AddScoped<ILogService, LogService>();



EmailReaderOptions emailReaderOptions = new EmailReaderOptions()
{
    AdminPassword = builder.Configuration["Admin:Password"],
    ServerUrl = builder.Configuration["ImportBatch:Server"],
    ImapServerUrl = builder.Configuration["ImportBatch:ImapServer"],
    ImapServerPort = 993,
    ImapUser = builder.Configuration["ImportBatch:ImapUser"],
    ImapPassword = builder.Configuration["ImportBatch:ImapPassword"],

};
builder.Services.AddSingleton<IEmailReader>(sp => new EmailReader(emailReaderOptions));

builder.Services.AddSingleton<ICleanDocs>(sp => new CleanDocs(emailReaderOptions));

// 1. Registra la connessione al DB (o il tuo IUserConnectionProvider dinamico)
builder.Services.AddScoped<IDbConnection>(sp => new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Registra il validatore specifico per l'interfaccia generica
builder.Services.AddScoped<IBasicAuthValidator, DatabaseAuthValidator>();

// 3. Registra l'autenticazione Basic (che troverà automaticamente IBasicAuthValidator)
builder.Services.AddAuthentication("BasicAuthentication")
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<ImportDocBackgroundService>();
builder.Services.AddHostedService<CleanDataBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddHttpClient("RenderService")
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(5); // Wait 5 seconds between tries
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // <-- Aggiungi questa riga
}

app.UseHttpsRedirection();

// 2. Registra i file delle rotte qui
app.MapTenantRoutes();
app.MapUserRoutes();
app.MapDocRoutes();
app.MapAdminRoutes();
app.MapFornitoreRoutes();
app.MapArticoliRoutes();
app.MapLogsRoutes();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();


// Enable serving static files (like index.html)
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Controlla se la richiesta è per il file index.html (o per la root del sito)
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            // Imposta gli header HTTP per evitare qualsiasi tipo di cache
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});

//Test
//app.MapGet("/", () => "Hello");

app.MapPost("/api/v1/auth", (ClaimsPrincipal claims) =>
    {
        if (claims.Identity == null || !claims.Identity.IsAuthenticated) return  Results.Ok(new LoginResponse(false, ""));

        return Results.Ok(new LoginResponse(true, ""));
    })
    .WithName("Auth");

app.Run();

