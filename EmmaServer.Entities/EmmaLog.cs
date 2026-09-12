using Dapper.Contrib.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmmaServer.Entities;

[Dapper.Contrib.Extensions.Table("log")]
public class EmmaLog : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Write(false)]
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "varchar(100)")]
    public string? tenant { get; set; } = string.Empty;

    public int token_input { get; set; } = 0;
    public int token_output{ get; set; } = 0;
    public int token_tot { get; set; } = 0;

    public double cost { get; set; }

    public int stato { get; set; } = 0;

    public string? message { get; set; } = string.Empty;

    public long duration { get; set; } = 0;

    public int modulo { get; set; } = 0;
}
