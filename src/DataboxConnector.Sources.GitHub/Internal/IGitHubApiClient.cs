using DataboxConnector.Sources.GitHub.Models;

namespace DataboxConnector.Sources.GitHub.Internal;

/// <summary>
/// Wrapper around the GitHub REST endpoints used for ingestion.
/// </summary>
/// <remarks>
/// Yields results as async streams so callers can begin processing the first
/// page while subsequent pages are being fetched.
/// </remarks>
internal interface IGitHubApiClient
{
    /// <summary>
    /// Streams commits authored after <paramref name="since"/> (UTC).
    /// </summary>
    /// <param name="repository">Repository to query.</param>
    /// <param name="since">Inclusive lower bound for commit author date.</param>
    /// <param name="includeStats">
    /// When <c>true</c>, performs an additional <c>GET /commits/{sha}</c> per
    /// commit to populate <c>additions/deletions/total</c>.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    IAsyncEnumerable<GitHubCommit> GetCommitsAsync(
        GitHubRepository repository,
        DateTimeOffset since,
        bool includeStats,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams pull requests in any state, sorted by update time descending.
    /// Stops yielding once a PR older than <paramref name="since"/> is reached
    /// (since the API sorts newest first).
    /// </summary>
    IAsyncEnumerable<GitHubPullRequest> GetPullRequestsAsync(
        GitHubRepository repository,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}