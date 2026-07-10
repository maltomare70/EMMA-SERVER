using EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IEmmaService
{
    Task InitAsync();
    Task TestAsync();
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
    
    public async Task TestAsync()
    {
        await _emmaRepository.TestAsync();
    }
}
