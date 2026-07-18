using System.ComponentModel.DataAnnotations;
using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

[Table("log")]
public class EmmaLog : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Write(false)]
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;

    public string? tenant { get; set; } = string.Empty;

    public int token_input { get; set; } = 0;
    public int token_output{ get; set; } = 0;
    public int token_tot { get; set; } = 0;

    public double cost { get; set; }

    public int stato { get; set; } = 0;

    public string? message { get; set; } = string.Empty;
}
