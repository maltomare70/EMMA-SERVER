using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmmaServer;

public interface IBasicAuthValidator
{
    Task<AuthValidationResult> ValidaCredenzialiAsync(string username, string password);
}

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IBasicAuthValidator _authValidator;
    private readonly string? _adminPassword;
    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IBasicAuthValidator authValidator, IConfiguration configuration) : base(options, logger, encoder)
    {
        _authValidator = authValidator;

        _adminPassword = configuration["Admin:Password"];
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return AuthenticateResult.Fail("Header Authorization mancante.");

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]!);
            
            if (authHeader.Scheme != "Basic")
                return AuthenticateResult.Fail("Schema di autenticazione non valido. Usare 'Basic'.");

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
            
            if (credentials.Length != 2)
                return AuthenticateResult.Fail("Formato delle credenziali non valido.");

            var username = credentials[0];
            var password = credentials[1];

            if (username.Equals("admin", StringComparison.InvariantCultureIgnoreCase) && password == _adminPassword)
            {
                string tenant_code = string.Empty;
                if (!Request.Headers.ContainsKey("x-tenant"))
                    tenant_code = "emma"; // Or your default tenant identifier
                else
                    tenant_code = Request.Headers["x-tenant"].ToString();
                
                // Se valido, creiamo l'identità dell'utente
                var claims = new[] {
                    new Claim(ClaimTypes.Name, username),
                    new Claim("database_name", "emma"),
                    new Claim("tenant", tenant_code)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return AuthenticateResult.Success(ticket);

            }
            else
            {
                // Utilizziamo il validatore generico iniettato (va cambiato)
                //con l'utente e la password bisogna validare e restituire il nome del database dell'utente
                //si deve usare un database con connessione statica o un altro sistema json file
                var result = await _authValidator.ValidaCredenzialiAsync(username, password);

                bool isValid = result.IsValid;

                if (!isValid) return AuthenticateResult.Fail("Credenziali errate.");

                // Se valido, creiamo l'identità dell'utente
                var claims = new[] {
                    new Claim(ClaimTypes.Name, username),
                    new Claim("database_name", result.DatabaseName),
                    new Claim("tenant", result.Tenant)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return AuthenticateResult.Success(ticket);
            }
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail($"Errore di autenticazione: {ex.Message}");
        }
    }
}