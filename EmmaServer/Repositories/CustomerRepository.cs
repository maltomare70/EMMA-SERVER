
using Dapper; // Necessario per usare il metodo esteso QueryAsync
using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface ICustomerRepository : IRepositoryGenerico<Customer>
{
    // Aggiungi qui la firma del tuo nuovo metodo personalizzato
    Task<IEnumerable<Customer>> GetByNomeAsync(string nome);
}

// 2. Implementiamo la classe che eredita dal Repository Generico
public class CustomerRepository : RepositoryGenerico<Customer>, ICustomerRepository
{
    // Il costruttore accetta il provider dinamico e lo gira alla classe base
    public CustomerRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    // Il tuo nuovo metodo personalizzato
    public async Task<IEnumerable<Customer>> GetByNomeAsync(string nome)
    {
        // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
        const string sql = "SELECT id, nome FROM customer WHERE nome = @Nome;";

        // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
        using var db = CreaConnessione();
        
        // Eseguiamo una normale query Dapper (non Contrib)
        return await db.QueryAsync<Customer>(sql, new { Nome = nome });
    }
}