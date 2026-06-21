using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IFornitoriRepository : IRepositoryGenerico<EmmaFornitori>
{

}

public class FornitoriRepository : RepositoryGenerico<EmmaFornitori>, IFornitoriRepository
{
    public FornitoriRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

}