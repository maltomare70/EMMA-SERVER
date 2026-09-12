using Dapper.Contrib.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Dapper.Contrib.Extensions.Table("tenants")] 
public record EmmaTenant: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Required]
    [Column(TypeName = "varchar(100)")]
    public string codice { get; init; } = string.Empty;
    [Required]
    [Column(TypeName = "varchar(256)")]
    public string? descrizione { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; set; }= DateTime.UtcNow;
    [Column(TypeName = "varchar(256)")]
    public string? mail_from { get; set; } = string.Empty;
}