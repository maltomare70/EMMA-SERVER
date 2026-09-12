using Dapper.Contrib.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Dapper.Contrib.Extensions.Table("users")] 
public record EmmaUser : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Required]
    [Column(TypeName = "varchar(256)")]
    public string email { get; init; } = string.Empty;
    [Required]
    [Column(TypeName = "varchar(100)")]
    public string? pwd { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "varchar(100)")]
    public string? tenant { get; set; }
    
    [Column(TypeName = "varchar(256)")]
    public string? codice { get; set; }

    public bool enabled { get; set; } = false;
}

