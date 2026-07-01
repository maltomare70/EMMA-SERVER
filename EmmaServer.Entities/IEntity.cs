
using Dapper.Contrib.Extensions;
namespace EmmaServer.Entities;

public interface IEntity
{
    int id { get; set; }
    [Write(false)] 
    public DateTime data_creazione { get; set; }
}
