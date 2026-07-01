using System.ComponentModel.DataAnnotations;
using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Table("users")] 
public record EmmaUser : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Required]
    public string email { get; init; } = string.Empty;
    [Required]
    public string? pwd { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    public string? tenant { get; set; }
}