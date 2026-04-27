using DataboxConnector.Core.Abstractions;
using DataboxConnector.Sinks.Databox.Configuration;
using DataboxConnector.Sinks.Databox.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sinks.Databox.DependencyInjection;

/// <summary>
/// Registers the Databox sink and all of its dependencies into the DI container.
/// </summary>
public static class DataboxSinkServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DataboxSink"/> as an <see cref="ISinkConnector"/>,
    /// configures the typed HTTP client with auth and resilience, and wires up
    /// the local identifier store.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configuration">
    /// Configuration root; the <c>Databox</c> section is bound to <see cref="DataboxOptions"/>.
    /// </param>
    public static IServiceCollection AddDataboxSink(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 1. Bind options + validate at startup.
        services
            .AddOptions<DataboxOptions>()
            .Bind(configuration.GetSection(DataboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. Register identifier store as singleton (caches identifiers in memory).
        services.AddSingleton<IDataboxIdentifierStore, FileBasedIdentifierStore>();

        // 3. Register typed HttpClient for the API client. The handler lifetime
        //    is managed by HttpClientFactory, so we never new() a HttpClient.
        services
            .AddHttpClient<IDataboxApiClient, DataboxApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<DataboxOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            // 4. Resilience: retry transient failures, with backoff, plus a per-attempt timeout.
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.UseJitter = true;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            });

        // 5. Register the sink itself. It depends on the API client + identifier store.
        services.AddSingleton<ISinkConnector, DataboxSink>();
        services.AddSingleton<DataboxSink>(sp => (DataboxSink)sp.GetRequiredService<ISinkConnector>());

        return services;
    }
}