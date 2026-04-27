using System.Runtime.CompilerServices;
using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using DataboxConnector.Sources.GitHub.Configuration;
using DataboxConnector.Sources.GitHub.Internal;
using DataboxConnector.Sources.GitHub.Mapping;
using DataboxConnector.Sources.GitHub.Models;
using DataboxConnector.Sources.GitHub.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sources.GitHub;

/// <summary>
/// Source connector that extracts pull requests from one or more GitHub repositories.
/// </summary>
public sealed class GitHubPullRequestsSource : ISourceConnector
{
    public string SourceName => "github_pull_requests";
    public DatasetSchema Schema => GitHubPullRequestsSchema.Instance;

    private readonly IGitHubApiClient _api;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubPullRequestsSource> _logger;

    internal GitHubPullRequestsSource(
        IGitHubApiClient api,
        IOptions<GitHubOptions> options,
        ILogger<GitHubPullRequestsSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<RawRecord> ExtractAsync(
        ExtractionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var since = context.From ?? DateTimeOffset.UtcNow.AddDays(-_options.DefaultLookbackDays);
        var emitted = 0;

        _logger.LogInformation(
            "Extracting GitHub pull requests since {Since} from {Count} repos.",
            since, _options.Repositories.Count);

        foreach (var repoSpec in _options.Repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GitHubRepository repo;
            try
            {
                repo = GitHubRepository.Parse(repoSpec);
            }
            catch (ArgumentException ex)
            {
                throw new SourceExtractionException(SourceName,
                    $"Invalid repository '{repoSpec}': {ex.Message}", ex);
            }

            var perRepoCount = 0;

            await foreach (var pr in _api
                .GetPullRequestsAsync(repo, since, cancellationToken)
                .ConfigureAwait(false))
            {
                if (emitted >= _options.MaxItemsPerRun)
                {
                    _logger.LogWarning(
                        "Reached MaxItemsPerRun ({Cap}); halting PR extraction.",
                        _options.MaxItemsPerRun);
                    yield break;
                }

                var record = PullRequestMapper.Map(pr, repo.FullName);
                if (record is null)
                {
                    _logger.LogDebug(
                        "Skipping malformed PR #{Number} in {Repo}.", pr.Number, repo.FullName);
                    continue;
                }

                yield return record;
                emitted++;
                perRepoCount++;
            }

            _logger.LogInformation(
                "Extracted {Count} pull requests from {Repo}.", perRepoCount, repo.FullName);
        }

        _logger.LogInformation(
            "GitHub pull requests extraction completed: {Total} total records.", emitted);
    }
}