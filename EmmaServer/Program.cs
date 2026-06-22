using EmmaServer;
using EmmaServer.Repositories;
using EmmaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Npgsql;
using System.Data;
using Dapper;
using EmmaServer.Endpoints;
using EmmaServer.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

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

// 2. Registra i file delle rotte qui
app.MapTenantRoutes();
app.MapUserRoutes();
app.MapDocRoutes();
app.MapAdminRoutes();
app.MapFornitoreRoutes();
app.MapArticoliRoutes();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

//Test
app.MapGet("/", () => "Hello");

app.MapPost("/api/v1/auth", (ClaimsPrincipal claims) =>
    {
        if (claims.Identity == null || !claims.Identity.IsAuthenticated) return  Results.Ok(new LoginResponse(false, ""));

        return Results.Ok(new LoginResponse(true, ""));
    })
    .WithName("Auth");

app.Run();

