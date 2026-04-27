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
/// Source connector that extracts commits from one or more GitHub repositories.
/// </summary>
/// <remarks>
/// Iterates the configured repositories sequentially. Within each repository,
/// commits are streamed page by page; when <see cref="GitHubOptions.IncludeCommitStats"/>
/// is enabled, an additional detail call per commit populates the change stats.
/// </remarks>
public sealed class GitHubCommitsSource : ISourceConnector
{
    public string SourceName => "github_commits";
    public DatasetSchema Schema => GitHubCommitsSchema.Instance;

    private readonly IGitHubApiClient _api;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubCommitsSource> _logger;

    internal GitHubCommitsSource(
        IGitHubApiClient api,
        IOptions<GitHubOptions> options,
        ILogger<GitHubCommitsSource> logger)
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
            "Extracting GitHub commits since {Since} from {Count} repos (includeStats={Stats}).",
            since, _options.Repositories.Count, _options.IncludeCommitStats);

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

            await foreach (var commit in _api
                .GetCommitsAsync(repo, since, _options.IncludeCommitStats, cancellationToken)
                .ConfigureAwait(false))
            {
                if (emitted >= _options.MaxItemsPerRun)
                {
                    _logger.LogWarning(
                        "Reached MaxItemsPerRun ({Cap}); halting commit extraction. " +
                        "Increase the cap or shorten the time window if this happens often.",
                        _options.MaxItemsPerRun);
                    yield break;
                }

                var record = CommitMapper.Map(commit, repo.FullName);
                if (record is null)
                {
                    _logger.LogDebug("Skipping malformed commit in {Repo}.", repo.FullName);
                    continue;
                }

                yield return record;
                emitted++;
                perRepoCount++;
            }

            _logger.LogInformation(
                "Extracted {Count} commits from {Repo}.", perRepoCount, repo.FullName);
        }

        _logger.LogInformation("GitHub commits extraction completed: {Total} total records.", emitted);
    }
}