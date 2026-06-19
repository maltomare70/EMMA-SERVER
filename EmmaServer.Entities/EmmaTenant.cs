using System.ComponentModel.DataAnnotations;
using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Table("tenants")] 
public record EmmaTenant: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    [Required]
    public string codice { get; init; } = string.Empty;
    [Required]
    public string? descrizione { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; init; }= DateTime.UtcNow;
}