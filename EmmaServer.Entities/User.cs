using System.ComponentModel.DataAnnotations;
using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Table("users")] 
public record User : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    public string email { get; init; } = string.Empty;
    [Required]
    public string pwd { get; init; }
    [Write(false)] 
    public DateTime created_at { get; init; }

}