using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmmaServer;

// Modello per mappare l'oggetto JSON
public record JsonUserCredentials(string Username, string PasswordHash, string DatabaseName);


// Modifichiamo leggermente l'output del validatore per passarci dietro il nome del database
public record AuthValidationResult(bool IsValid, string? DatabaseName);

public class JsonFileAuthValidator : IBasicAuthValidator
{
    private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "users.json");

    public async Task<AuthValidationResult> ValidaCredenzialiAsync(string username, string password)
    {
        if (!File.Exists(_filePath))
        {
            throw new FileNotFoundException("Il file di configurazione utenti users.json è mancante.");
        }

        // Leggiamo il file JSON in modo asincrono
        using var stream = File.OpenRead(_filePath);
        var opzioni = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var utenti = await JsonSerializer.DeserializeAsync<List<JsonUserCredentials>>(stream, opzioni);

        if (utenti == null) return new AuthValidationResult(false, null);

        // Cerchiamo l'utente corrispondente
        var utente = utenti.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        // Verifica delle credenziali (sostituisci con BCrypt in produzione)
        if (utente != null && utente.PasswordHash == password)
        {
            // Restituiamo successo e il nome del database associato!
            return new AuthValidationResult(true, utente.DatabaseName);
        }

        return new AuthValidationResult(false, null);
    }
}
