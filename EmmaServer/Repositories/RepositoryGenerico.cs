using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using Npgsql;
using EmmaServer.Entities;

namespace EmmaServer.Repositories;


public interface IRepositoryGenerico<T> where T : class, IEntity
{
    Task<int> AddAsync(T entita);
    Task<T?> GetIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<bool> UpdateAsync(T entita);
    Task<bool> DeleteAsync(T entita);
}

public class RepositoryGenerico<T> : IRepositoryGenerico<T> where T : class, IEntity
{
    private readonly string _connectionString;

    private readonly IUserConnectionProvider _connectionProvider;
    
    public RepositoryGenerico(IUserConnectionProvider connectionProvider)
    {
        //_connectionString = "Host=localhost:5432;Username=marco;Password=malt0mare;Database=nome_db";
        _connectionProvider = connectionProvider;
        
        // Chicca: Questo dice a Dapper di mappare automaticamente 
        // le proprietà PascalCase (C#) con le colonne snake_case (Postgres)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    protected IDbConnection CreaConnessione() 
    {
        string stringaDinamica = _connectionProvider.GetConnectionString();
        return new NpgsqlConnection(stringaDinamica);
    }

    // CREATE
    public async Task<int> AddAsync(T entita)
    {
        using var db = CreaConnessione();
        // Inserisce l'entità e restituisce l'ID generato (long, che castiamo a int)
        long idGenerato = await db.InsertAsync(entita);
        return (int)idGenerato;
    }

    // READ (Singolo)
    public async Task<T?> GetIdAsync(int id)
    {
        using var db = CreaConnessione();
        return await db.GetAsync<T>(id);
    }

    // READ (Tutti)
    public async Task<IEnumerable<T>> GetAllAsync()
    {
        using var db = CreaConnessione();
        return await db.GetAllAsync<T>();
    }

    // UPDATE
    public async Task<bool> UpdateAsync(T entita)
    {
        using var db = CreaConnessione();
        // Ritorna true se l'aggiornamento è andato a buon fine
        return await db.UpdateAsync(entita);
    }

    // DELETE
    public async Task<bool> DeleteAsync(T entita)
    {
        using var db = CreaConnessione();
        return await db.DeleteAsync(entita);
    }
}
