using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface ITenantRepository
{

}
public class TenantRepository : RepositoryGenerico<EmmaTenant>, ITenantRepository
{
    public TenantRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }
}
