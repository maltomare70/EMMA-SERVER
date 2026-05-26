
using Dapper.Contrib.Extensions;
namespace EmmaServer.Entities;

public interface IEntity
{
    int id { get; init; }
    [Write(false)] 
    public DateTime created_at { get; init; }
}
