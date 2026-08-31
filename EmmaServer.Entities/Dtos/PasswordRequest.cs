namespace EmmaServer.Entities.Dtos;

public record PasswordRequest(string Password);

public record LoginResponse(bool esito, string url);