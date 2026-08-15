using Emma.Services.Http;
using EmmaServer.Entities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Emma.Services.Services;

public interface IAdminServiceClient
{

}
public class AdminServiceClient : ServiceClientBase, IAdminServiceClient
{
    private const string EndpointInit = "/api/database/init";

    public AdminServiceClient(string url, string user, string password)
    : base(url, user, password)
    {
    }

    public AdminServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    /// <inheritdoc />
    protected override Exception CreateError(HttpResponseMessage response, string body)
        => InvioError(response, body);


    public async Task InitAsync()
    => await PostAsync(EndpointInit).ConfigureAwait(false);
}
