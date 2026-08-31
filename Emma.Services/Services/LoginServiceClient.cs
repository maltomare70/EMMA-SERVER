using Emma.Services.Http;
using EmmaServer.Entities.Dtos;

namespace EmmaClientAv.Services;

public interface ILoginServiceClient
{
    Task<LoginResponse> LoginAsync();
}

public class LoginServiceClient : ServiceClientBase, ILoginServiceClient
{
    private const string Endpoint = "/api/v1/auth";

    public LoginServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public LoginServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    public async Task<LoginResponse> LoginAsync()
        => (await PostAsync<LoginResponse>(Endpoint, error: InvioErrorLegacy).ConfigureAwait(false))!;
}
