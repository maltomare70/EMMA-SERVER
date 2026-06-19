using EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IEmmaService
{
    Task InitAsync();
}

public class EmmaService: IEmmaService
{
    private readonly IEmmaRepository _emmaRepository;
    public EmmaService(IEmmaRepository emmaRepository)
    {
        _emmaRepository = emmaRepository;
    }

    public async Task InitAsync()
    {
        await _emmaRepository.InitializeAsync();
    }
}
