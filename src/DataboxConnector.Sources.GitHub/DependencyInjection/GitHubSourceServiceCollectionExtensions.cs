using DataboxConnector.Sources.GitHub.Configuration;
using DataboxConnector.Sources.GitHub.Internal;
using DataboxConnector.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sources.GitHub.DependencyInjection;

/// <summary>
/// Registers the GitHub source(s) and their dependencies.
/// </summary>
public static class GitHubSourceServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the typed HTTP client, options binding, and source connectors
    /// for GitHub.
    /// </summary>
    public static IServiceCollection AddGitHubSources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 1. Bind + validate GitHubOptions.
        services
            .AddOptions<GitHubOptions>()
            .Bind(configuration.GetSection(GitHubOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. Typed HttpClient with PAT auth, user agent, and base URL.
        services
            .AddHttpClient<IGitHubApiClient, GitHubApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<GitHubOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.PersonalAccessToken}");
                client.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler(o =>
            {
                // GitHub limits authenticated requests to 5000/hour and applies
                // secondary rate limits per endpoint. Be polite: retry conservatively
                // and rely on backoff for 429/abuse-detection responses.
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.UseJitter = true;
                o.Retry.Delay = TimeSpan.FromSeconds(2);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
            });

        // 3. Source connectors. Registered as both ISourceConnector (for the
        //    pipeline orchestrator to discover them) and as their concrete types
        //    (for jobs that target a specific source).
        services.AddSingleton<GitHubCommitsSource>();
        services.AddSingleton<GitHubPullRequestsSource>();

        services.AddSingleton<ISourceConnector>(sp => sp.GetRequiredService<GitHubCommitsSource>());
        services.AddSingleton<ISourceConnector>(sp => sp.GetRequiredService<GitHubPullRequestsSource>());

        return services;
    }
}