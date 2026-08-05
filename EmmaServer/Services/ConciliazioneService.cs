using EmmaServer.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Diagnostics;

namespace EmmaServer.Services;

public interface IConciliazioneService
{
    Task<PayloadRiconciliazione> GetConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture);
    Task<ConciliazioneResponse> GetConciliazioneBolleFattureAsync(InputConciliazione inputConciliazione, string tenant);
}
public class ConciliazioneService : IConciliazioneService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogService _logService;
    public ConciliazioneService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogService logService)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logService = logService;
    }

    public async Task<PayloadRiconciliazione> GetConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture)
    {
        // Step 1: Match esatto in .NET (fornitore + codice articolo + tolleranza qta)
        var matching = await GetConciliazioneMatchEsatto(bolle, fatture);
        var matchingBolle = matching.bolle.ToList();
        var matchingFatture = matching.fatture.ToList();
        var nonMatchingBolle = bolle
            .ExceptBy(matchingBolle.Select(b => b.IdRiga), b => b.IdRiga)
            .ToList();
        var nonMatchingFatture = fatture
               .ExceptBy(matchingFatture.Select(f => f.IdRiga), f => f.IdRiga)
               .ToList();

        // Step 2: Se ci sono non-matching, chiedi al servizio Python (fuzzy matching)
        if (nonMatchingBolle.Any() && nonMatchingFatture.Any())
        {
            var fuzzyResults = await ChiamaPythonFuzzy(nonMatchingBolle, nonMatchingFatture);
            matching.bolle.AddRange(fuzzyResults.Select(x => x.Item1));
            matching.fatture.AddRange(fuzzyResults.Select(x => x.Item2));
        }

        return matching;
    }

    private async Task<PayloadRiconciliazione> GetConciliazioneMatchEsatto(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture)
    {
        var matches = bolle
            .Join(fatture,
                b => new { b.Fornitore, b.CodiceArticolo },
                f => new { f.Fornitore, f.CodiceArticolo },
                (b, f) => new { b, f })
            .Where(x => Math.Abs(x.b.Qta - x.f.Qta) < 0.01)
            .ToList();

        return new PayloadRiconciliazione
        {
            bolle = matches.Select(x => x.b).ToList(),
            fatture = matches.Select(x => x.f).ToList()
        };
    }

    private async Task<List<(RigaConciliazione, RigaConciliazione)>> ChiamaPythonFuzzy(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture)
    {
        try
        {
            var fuzzyMatches = await GetConciliazioneFuzzyAsync(new PayloadRiconciliazione
            {
                bolle = bolle,
                fatture = fatture
            });

            if (fuzzyMatches is null || fuzzyMatches.Count == 0) return new();

            var risultato = new List<(RigaConciliazione, RigaConciliazione)>();
            foreach (var match in fuzzyMatches)
            {
                var bolla = bolle.FirstOrDefault(b => b.IdRiga == match.bolla_id);
                var fattura = fatture.FirstOrDefault(f => f.IdRiga == match.fattura_id);

                if (bolla is not null && fattura is not null)
                    risultato.Add((bolla, fattura));
            }

            return risultato;
        }
        catch (Exception ex)
        {
            // Servizio Python non raggiungibile: continua senza fuzzy
            return new();
        }
    }

    public async Task<List<FuzzyMatchResult>> GetConciliazioneFuzzyAsync(PayloadRiconciliazione payloadRiconciliazioneFuzzy)
    {
        var client = _httpClientFactory.CreateClient();
        var url = _configuration["EMMA-AI:EndPoint"]; //https://emma-aegc.onrender.com",
        var externalApiUrl = $"{url}/api/v1/riconcilia/bolle-fatture";

        using var request = new HttpRequestMessage(HttpMethod.Post, externalApiUrl);

        // ADD YOUR HEADERS HERE
        var model = _configuration["EMMA-AI:Model"];
        request.Headers.Add("x-model", model);
        var apiKey = _configuration["EMMA-AI:ApiKey"];
        request.Headers.Add("X-API-Key", apiKey);

        request.Content = JsonContent.Create(payloadRiconciliazioneFuzzy);
        HttpResponseMessage response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var fuzzyMatchResults = await response.Content.ReadFromJsonAsync<List<FuzzyMatchResult>>();
            return fuzzyMatchResults ?? new List<FuzzyMatchResult>();
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="inputConciliazione"></param>
    /// <returns></returns>
    /// <exception cref="ApplicationException"></exception>
    public async Task<ConciliazioneResponse> GetConciliazioneBolleFattureAsync(InputConciliazione inputConciliazione, string tenant)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = _configuration["EMMA-AI:EndPoint"]; //https://emma-aegc.onrender.com",
            var externalApiUrl = $"{url}/api/v1/riconcilia/bolle-fatture";

            using var request = new HttpRequestMessage(HttpMethod.Post, externalApiUrl);

            // ADD YOUR HEADERS HERE
            var model = _configuration["EMMA-AI:Model"];
            request.Headers.Add("x-model", model);
            var apiKey = _configuration["EMMA-AI:ApiKey"];
            request.Headers.Add("X-API-Key", apiKey);

            request.Content = JsonContent.Create(inputConciliazione);
            HttpResponseMessage response = await client.SendAsync(request);

            stopwatch.Stop();
            long secondiInteri = stopwatch.ElapsedMilliseconds / 1000;

            if (response.IsSuccessStatusCode)
            {
                var results = await response.Content.ReadFromJsonAsync<ConciliazioneResponse>();

                //**************
                //LOG SUCCESS AI
                //**************
                await _logService.AddAsync(new EmmaLog()
                {
                    stato = 1,
                    tenant = tenant,
                    token_input = results.Costs.PromptTokens,
                    token_output = results.Costs.OutputTokens,
                    token_tot = results.Costs.TotalTokens,
                    cost = results.Costs.TotalCostEur,
                    message = $"{inputConciliazione.Fornitore}",
                    duration = secondiInteri
                });

                return results ?? new ConciliazioneResponse();
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();

                //**************
                //LOG ERRORE AI
                //**************

                await _logService.AddAsync(new EmmaLog()
                {
                    stato = -1,
                    tenant = tenant,
                    token_input = 0,
                    token_output = 0,
                    token_tot = 0,
                    cost = 0,
                    message = $"{response.StatusCode} - {response.Content}",
                    duration = secondiInteri
                });

                throw new ApplicationException(errorContent);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            long secondiInteri = stopwatch.ElapsedMilliseconds / 1000;
            //**************
            //LOG ERRORE AI
            //**************
            await _logService.AddAsync(new EmmaLog()
            {
                stato = -1,
                tenant = tenant,
                token_input = 0,
                token_output = 0,
                token_tot = 0,
                cost = 0,
                message = $"{ex.Message}",
                duration = secondiInteri
            });
            throw new ApplicationException(ex.Message);
        }
    }
}
