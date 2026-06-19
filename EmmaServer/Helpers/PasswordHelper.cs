using Microsoft.AspNetCore.Identity;

namespace EmmaServer.Helpers;

public static class PasswordHelper
{
    public static string GeneraHash(string password)
    {

        var passwordHasher = new PasswordHasher<object>();

        string hashedPassword = passwordHasher.HashPassword(null, password);

        return hashedPassword;
    }

    public static bool VerificaPassword(string passwordInseritaUtente, string hashedPasswordFromDb)
    {
        var passwordHasher = new PasswordHasher<object>();

        var result = passwordHasher.VerifyHashedPassword(
            null,
            hashedPasswordFromDb,
            passwordInseritaUtente
        );

        if (result == PasswordVerificationResult.Success)
        {
            return true;
        }
        else
        {
            return false;
        }


    }
}
