using Dapper.Contrib.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json;

namespace EmmaServer.Entities;

[Dapper.Contrib.Extensions.Table("conciliamaster")]
public record EmmaConciliaMaster : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Column(TypeName = "varchar(100)")]
    public string tenant { get; set; } = string.Empty;
    [Write(false)]
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "varchar(256)")]
    public string codice { get; init; } = string.Empty;

    public JsonDocument? content { get; set; }   
}


[Dapper.Contrib.Extensions.Table("conciliarighe")]
public record EmmaConciliaRighe: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string id_master { get; set; } = string.Empty;
    [Column(TypeName = "varchar(100)")]
    public string id_riga { get; set; } = string.Empty;
    [Column(TypeName = "varchar(100)")]
    public string tenant { get; set; } = string.Empty;
    [Write(false)]
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "varchar(256)")]
    public string codice { get; init; } = string.Empty;
    [Column(TypeName = "varchar(100)")]
    public string stato { get; init; } = string.Empty;
    public string note { get; init; } = string.Empty;

    [Column(TypeName = "varchar(100)")]
    public string id_fornitore { get; set; } = string.Empty;
    public int tipo_doc { get; set; } = 0;

    public decimal qta { get; set; } = 0;
    public decimal qta_canc { get; set; } = 0;
    public decimal delta { get; set; } = 0;

    public int flag { get; set; } = 0;

}


