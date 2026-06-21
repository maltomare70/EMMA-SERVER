using EmmaServer.Entities;
using Dapper; // Necessario per usare il metodo esteso QueryAsync
using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IUserRepository : IRepositoryGenerico<EmmaUser>
{
    // Aggiungi qui la firma del tuo nuovo metodo personalizzato
    Task<EmmaUser> GetByEmailAsync(string email);
}

public class UserRepository : RepositoryGenerico<EmmaUser>, IUserRepository
{
    public UserRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }   
    
    public async Task<EmmaUser> GetByEmailAsync(string email)
    {
        // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
        const string sql = "SELECT * FROM users WHERE email = @email;";

        // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
        using var db = await CreaConnessione();
        
        // Eseguiamo una normale query Dapper (non Contrib)
        return await db.QueryFirstAsync<EmmaUser>(sql, new { email = email });
    }

}