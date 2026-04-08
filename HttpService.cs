using MatrixRegistratorBot.Dto;
using Polly;
using Polly.Retry;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MatrixRegistratorBot;

internal class HttpService
{
    internal const string MasPrefix = "account";
    internal const string MatrixPrefix = "matrix";

    private readonly HttpClient _matrixHttpClient;

    private readonly HttpClient _masForTokenOnlyHttpClient;

    private readonly HttpClient _masHttpClient;

    private ResiliencePipeline<HttpResponseMessage>? _resiliencePipeline;

    internal int TimeOutMilliseconds { get; set; } = 30000;

    internal string HomeServerUrl { get; }

    public HttpService()
    {
        HomeServerUrl = Environment.GetEnvironmentVariable("MATRIX_HOMESERVER_URL")
            ?? throw new InvalidOperationException("Не задана переменная среды MATRIX_HOMESERVER_URL");

        var adminBasicToken = Environment.GetEnvironmentVariable("MATRIX_BOT_ADMIN_BASIC_AUTH")
            ?? throw new InvalidOperationException("Не задана переменная среды MATRIX_BOT_ADMIN_BASIC_AUTH");

        string? tempString = Environment.GetEnvironmentVariable("MATRIX_BOT_USER_TIMEOUT");
        if (!string.IsNullOrEmpty(tempString))
        {
            TimeOutMilliseconds = int.Parse(tempString);
        }

        _matrixHttpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromMilliseconds(TimeOutMilliseconds + 1000),
            BaseAddress = new Uri($"https://{MatrixPrefix}.{HomeServerUrl}"),
        };

        _masForTokenOnlyHttpClient = new HttpClient()
        {
            BaseAddress = new Uri($"https://{MasPrefix}.{HomeServerUrl}"),
            DefaultRequestHeaders =
            {
                { "Authorization", "Basic " + adminBasicToken },
            },
        };

        _masHttpClient = new HttpClient()
        {
            BaseAddress = new Uri($"https://{MasPrefix}.{HomeServerUrl}"),
        };
    }

    internal async ValueTask<AuthorizationService> AuthorizeAsync(string url, CancellationToken cancellationToken)
    {
        var authorizationService = new AuthorizationService(this);

        string bearer = await authorizationService.AuthorizeAsync(url, cancellationToken).ConfigureAwait(false);
        _matrixHttpClient.DefaultRequestHeaders.Clear();
        _matrixHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearer}");

        _resiliencePipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,

                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode == HttpStatusCode.Forbidden || r.StatusCode == HttpStatusCode.Unauthorized),

                OnRetry = async args =>
                {
                    Log.Information("Authorization error. Authorizing.");
                    bearer = await authorizationService.AuthorizeAsync(url, args.Context.CancellationToken).ConfigureAwait(false);
                    _matrixHttpClient.DefaultRequestHeaders.Clear();
                    _matrixHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearer}");
                },
            }).Build();

        return authorizationService;
    }

    internal async ValueTask<HttpResponseMessage> PostAsync(string url, HttpContent? content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_resiliencePipeline, "MatrixService not initialized.");

        var response = await _resiliencePipeline.ExecuteAsync(
            async token =>
            {
                var resp = await _matrixHttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                return resp;
            },
            cancellationToken).ConfigureAwait(false);

        return response;
    }

    internal async ValueTask<HttpResponseMessage> PostNoRetryAsync(string url, HttpContent content, CancellationToken cancellationToken)
    {
        var response = await _matrixHttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        return response;
    }

    internal async ValueTask<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_resiliencePipeline, "MatrixService not initialized.");

        var response = await _resiliencePipeline.ExecuteAsync(
            async token =>
            {
                var resp = await _matrixHttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                return resp;
            },
            cancellationToken).ConfigureAwait(false);

        await HealthService.HeartBeatAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    internal async ValueTask<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var response = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<string?> AuthorizeAdminAsync(CancellationToken cancellationToken)
    {
        var data = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("scope", "urn:mas:admin")
        ]);

        var response = await _masForTokenOnlyHttpClient.PostAsync("/oauth2/token", data, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(
            Json.AppDtoContext.Default.AdminToken,
            cancellationToken).ConfigureAwait(false);

        return result?.AccessToken;
    }

    internal async ValueTask<CreatedUserResult> CreateUserAsync(string userName, string token, CancellationToken cancellationToken)
    {
        var jsonData = JsonContent.Create(
            new Dictionary<string, object>
            {
                { "username", userName },
                { "skip_homeserver_check", false },
            },
            Json.AppDtoContext.Default.DictionaryStringObject);

        _masHttpClient.DefaultRequestHeaders.Clear();
        _masHttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

        var response = await _masHttpClient.PostAsync("/api/admin/v1/users", jsonData, cancellationToken).ConfigureAwait(false);

        CreatedUserWrapper? result = null;
        if (response.StatusCode == HttpStatusCode.Created)
        {
            result = (await response.Content.ReadFromJsonAsync(Json.AppDtoContext.Default.CreatedUserWrapper, cancellationToken).ConfigureAwait(false))!;
        }
        else
        {
            Log.Error(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        return new CreatedUserResult { Id = result?.Data.Id, StatusCode = response.StatusCode, UserName = userName };
    }

    internal async ValueTask<CreateResult> SetUserPasswordAsync(string userId, string password, string token, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object>
            {
                { "password", password },
                { "skip_password_check", false },
            };
        var jsonData = JsonContent.Create(data, Json.AppDtoContext.Default.DictionaryStringObject);

        _masHttpClient.DefaultRequestHeaders.Clear();
        _masHttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

        var response = await _masHttpClient.PostAsync($"/api/admin/v1/users/{userId}/set-password", jsonData, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log.Error(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        return new CreateResult { StatusCode = response.StatusCode };
    }
}
