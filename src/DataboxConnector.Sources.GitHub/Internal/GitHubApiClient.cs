using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.GitHub.Models;
using Microsoft.Extensions.Logging;

namespace DataboxConnector.Sources.GitHub.Internal;

/// <summary>
/// HTTP-backed implementation of <see cref="IGitHubApiClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configured by <see cref="DependencyInjection.GitHubSourceServiceCollectionExtensions"/>
/// with the base URL, PAT, and resilience policies.
/// </para>
/// <para>
/// Pagination is driven by the <c>Link</c> header (<c>rel="next"</c>), as required
/// by the GitHub API guidelines, rather than incrementing page numbers manually.
/// </para>
/// </remarks>
internal sealed class GitHubApiClient : IGitHubApiClient
{
    private const string SourceName = "github";
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly ILogger<GitHubApiClient> _logger;

    public GitHubApiClient(HttpClient http, ILogger<GitHubApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _logger = logger;
    }

    public async IAsyncEnumerable<GitHubCommit> GetCommitsAsync(
        GitHubRepository repository,
        DateTimeOffset since,
        bool includeStats,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sinceParam = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var startUrl = $"/repos/{repository.Owner}/{repository.Name}/commits" +
                       $"?since={sinceParam}&per_page={PageSize}";

        await foreach (var commit in PaginateAsync<GitHubCommit>(startUrl, cancellationToken)
                                        .ConfigureAwait(false))
        {
            if (includeStats && !string.IsNullOrEmpty(commit.Sha))
            {
                var detail = await GetCommitDetailAsync(repository, commit.Sha!, cancellationToken)
                    .ConfigureAwait(false);

                if (detail is not null)
                {
                    // Merge detail (which has stats) with the list version (which has parents).
                    commit.Stats = detail.Stats;
                }
            }

            yield return commit;
        }
    }

    public async IAsyncEnumerable<GitHubPullRequest> GetPullRequestsAsync(
        GitHubRepository repository,
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // GitHub /pulls does not accept "since"; sorts by updated_at descending.
        // We stop iterating as soon as we see a PR older than `since`.
        var startUrl = $"/repos/{repository.Owner}/{repository.Name}/pulls" +
                       $"?state=all&sort=updated&direction=desc&per_page={PageSize}";

        var sinceUtc = since.ToUniversalTime().UtcDateTime;

        await foreach (var pr in PaginateAsync<GitHubPullRequest>(startUrl, cancellationToken)
                                    .ConfigureAwait(false))
        {
            // The API sorts by updated_at, but we filter on created_at to stay
            // consistent with our schema's "created_at" semantics.
            if (pr.CreatedAt.ToUniversalTime() < sinceUtc)
            {
                _logger.LogDebug(
                    "Reached PR older than {Since} ({Repo}#{Pr}); stopping pagination.",
                    sinceUtc, repository.FullName, pr.Number);
                yield break;
            }

            yield return pr;
        }
    }

    private async Task<GitHubCommit?> GetCommitDetailAsync(
        GitHubRepository repository,
        string sha,
        CancellationToken cancellationToken)
    {
        var url = $"/repos/{repository.Owner}/{repository.Name}/commits/{Uri.EscapeDataString(sha)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, $"GET {url}", cancellationToken).ConfigureAwait(false);

            return await response.Content
                .ReadFromJsonAsync<GitHubCommit>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SourceExtractionException ex)
        {
            // Don't fail the whole run for a single missing commit detail.
            _logger.LogWarning(ex, "Failed to fetch commit detail for {Sha}; stats will be missing.", sha);
            return null;
        }
    }

    private async IAsyncEnumerable<T> PaginateAsync<T>(
        string startUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class
    {
        var nextUrl = (Uri?)new Uri(startUrl, UriKind.Relative);

        while (nextUrl is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await _http.GetAsync(nextUrl, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, $"GET {nextUrl}", cancellationToken).ConfigureAwait(false);

            var items = await response.Content
                .ReadFromJsonAsync<List<T>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (items is null) yield break;

            foreach (var item in items)
                yield return item;

            nextUrl = LinkHeaderParser.GetNextUrl(response.Headers);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "no body" : body;

        // Differentiate auth failures so the operator gets a clear hint.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SourceExtractionException(SourceName,
                $"Authentication failed for {operation} (HTTP {statusCode}). " +
                $"Verify the personal access token. Detail: {Truncate(detail, 200)}");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SourceExtractionException(SourceName,
                $"{operation} returned 404. Verify the repository exists and the token has access.");
        }

        throw new SourceExtractionException(SourceName,
            $"{operation} failed with HTTP {statusCode}: {Truncate(detail, 500)}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}