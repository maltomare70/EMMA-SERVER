using Dapper.Contrib.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmmaServer.Entities;

// Specifichiamo il nome esatto della tabella su Postgres
[Dapper.Contrib.Extensions.Table("customer")] 
public record Customer : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }

    [Column(TypeName = "varchar(256)")]
    public string name { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;

}