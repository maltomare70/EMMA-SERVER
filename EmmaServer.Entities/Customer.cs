using System.ComponentModel.DataAnnotations;
using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Table("customer")] 
public record Customer : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    public string name { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; init; } = DateTime.UtcNow;

}