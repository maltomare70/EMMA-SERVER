using System;
using System.Collections.Generic;
using System.Text;

namespace EmmaServer.Entities.Dtos;

public class EmmaConciliaRigheDto
{
    public int id { get; set; }
    public string id_master { get; set; } = string.Empty;
    public string id_riga { get; set; } = string.Empty;
    public string tenant { get; set; } = string.Empty;

    public DateTime data_creazione { get; set; } = DateTime.UtcNow;

    public string codice { get; init; } = string.Empty;
    public string stato { get; init; } = string.Empty;
    public string note { get; init; } = string.Empty;

    public string id_fornitore { get; set; } = string.Empty;
    public int tipo_doc { get; set; } = 0;

    public decimal qta { get; set; } = 0;
    public decimal qta_canc { get; set; } = 0;
    public decimal delta { get; set; } = 0;

    public int flag { get; set; } = 0;

    public string numero_doc_abbinamento { get; init; } = string.Empty;
    public string data_doc_abbinamento { get; init; } = string.Empty;


}
