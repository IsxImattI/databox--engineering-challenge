using DataboxConnector.Core.Abstractions;
using DataboxConnector.Sources.Spotify.Configuration;
using DataboxConnector.Sources.Spotify.Internal;
using DataboxConnector.Sources.Spotify.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sources.Spotify.DependencyInjection;

/// <summary>
/// Registers the Spotify source(s) and the OAuth pipeline.
/// </summary>
public static class SpotifySourceServiceCollectionExtensions
{
    private const string TokenHttpClientName = "Spotify.Accounts";

    /// <summary>
    /// Wires up options binding, OAuth token store and provider, the API
    /// client, and the source connectors.
    /// </summary>
    public static IServiceCollection AddSpotifySources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 1. Bind + validate options.
        services
            .AddOptions<SpotifyOptions>()
            .Bind(configuration.GetSection(SpotifyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. Token storage (file-based by default).
        services.AddSingleton<ISpotifyTokenStore, FileBasedSpotifyTokenStore>();

        // 3. Named HttpClient for the accounts (auth) endpoint. Used by the
        //    token provider for refresh exchanges. Kept separate from the API
        //    client so each can have its own base URL and resilience config.
        services
            .AddHttpClient(TokenHttpClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<SpotifyOptions>>().Value;
                client.BaseAddress = new Uri(options.AccountsBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.Retry.Delay = TimeSpan.FromSeconds(1);
                o.Retry.UseJitter = true;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            });

        // 4. Token provider — depends on the named HttpClient above.
        services.AddSingleton<ISpotifyTokenProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(TokenHttpClientName);

            return new SpotifyTokenProvider(
                store: sp.GetRequiredService<ISpotifyTokenStore>(),
                options: sp.GetRequiredService<IOptions<SpotifyOptions>>(),
                http: http,
                logger: sp.GetRequiredService<ILogger<SpotifyTokenProvider>>());
        });

        // 5. Typed HttpClient for the API endpoints. The Authorization header
        //    is set per-request by SpotifyApiClient using the token provider.
        services
            .AddHttpClient<ISpotifyApiClient, SpotifyApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<SpotifyOptions>>().Value;
                client.BaseAddress = new Uri(options.ApiBaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.Delay = TimeSpan.FromSeconds(1);
                o.Retry.UseJitter = true;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            });

        // 6. Source connectors, registered as both ISourceConnector (for
        //    pipeline discovery) and concrete types (for jobs).
        services.AddSingleton<SpotifyRecentlyPlayedSource>(sp => new SpotifyRecentlyPlayedSource(
            sp.GetRequiredService<ISpotifyApiClient>(),
            sp.GetRequiredService<IOptions<SpotifyOptions>>(),
            sp.GetRequiredService<ILogger<SpotifyRecentlyPlayedSource>>()));

        services.AddSingleton<SpotifyTopTracksSource>(sp => new SpotifyTopTracksSource(
            sp.GetRequiredService<ISpotifyApiClient>(),
            sp.GetRequiredService<IOptions<SpotifyOptions>>(),
            sp.GetRequiredService<ILogger<SpotifyTopTracksSource>>()));

        services.AddSingleton<ISourceConnector>(sp => sp.GetRequiredService<SpotifyRecentlyPlayedSource>());
        services.AddSingleton<ISourceConnector>(sp => sp.GetRequiredService<SpotifyTopTracksSource>());

        return services;
    }
}