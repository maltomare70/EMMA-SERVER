using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IBolleMasterRepository
{

}

public class BolleMasterRepository: RepositoryGenerico<BolleMaster>, IBolleMasterRepository
{
    public BolleMasterRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }
    
}

