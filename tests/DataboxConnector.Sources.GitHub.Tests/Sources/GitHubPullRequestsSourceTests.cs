using System.Runtime.CompilerServices;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Sources.GitHub;
using DataboxConnector.Sources.GitHub.Configuration;
using DataboxConnector.Sources.GitHub.Internal;
using DataboxConnector.Sources.GitHub.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataboxConnector.Sources.GitHub.Tests.Sources;

public class GitHubPullRequestsSourceTests
{
    private static GitHubPullRequestsSource NewSource(
        IGitHubApiClient api,
        GitHubOptions? overrides = null)
    {
        var options = Options.Create(overrides ?? new GitHubOptions
        {
            PersonalAccessToken = "tok",
            Repositories = new() { "octo/repo" },
            UserAgent = "test/1.0",
            DefaultLookbackDays = 7,
            MaxItemsPerRun = 100
        });

        return new GitHubPullRequestsSource(api, options, NullLogger<GitHubPullRequestsSource>.Instance);
    }

    private static GitHubPullRequest ValidApiPr(int number) => new()
    {
        Number = number,
        State = "open",
        Title = $"PR {number}",
        User = new GitHubUser { Login = "matt" },
        CreatedAt = DateTime.UtcNow
    };

    private static async IAsyncEnumerable<GitHubPullRequest> AsyncOf(
        IEnumerable<GitHubPullRequest> prs,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var pr in prs)
        {
            ct.ThrowIfCancellationRequested();
            yield return pr;
            await Task.Yield();
        }
    }

    [Fact]
    public void SourceName_IsGithubPullRequests()
    {
        var fake = new FakeApiClient();
        NewSource(fake).SourceName.Should().Be("github_pull_requests");
    }

    [Fact]
    public void Schema_IsGithubPullRequestsSchema()
    {
        var fake = new FakeApiClient();
        NewSource(fake).Schema.Key.Should().Be("github_pull_requests_v1");
    }

    [Fact]
    public async Task ExtractAsync_StreamsAllPrs()
    {
        var fake = new FakeApiClient();
        fake.EnqueuePullRequestsResponse(() => AsyncOf(new[] { ValidApiPr(1), ValidApiPr(2) }));

        var source = NewSource(fake);

        var records = new List<RawRecord>();
        await foreach (var r in source.ExtractAsync(new ExtractionContext()))
            records.Add(r);

        records.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExtractAsync_InvalidRepoFormat_Throws()
    {
        var fake = new FakeApiClient();
        var options = new GitHubOptions
        {
            PersonalAccessToken = "t",
            Repositories = new() { "bad-format" },
            UserAgent = "ua",
            DefaultLookbackDays = 7,
            MaxItemsPerRun = 100
        };
        var source = NewSource(fake, options);

        var act = async () =>
        {
            await foreach (var _ in source.ExtractAsync(new ExtractionContext())) { }
        };

        await act.Should().ThrowAsync<SourceExtractionException>();
    }

    private sealed class FakeApiClient : IGitHubApiClient
    {
        private readonly Queue<Func<IAsyncEnumerable<GitHubCommit>>> _commitResponses = new();
        private readonly Queue<Func<IAsyncEnumerable<GitHubPullRequest>>> _prResponses = new();

        public void EnqueueCommitsResponse(Func<IAsyncEnumerable<GitHubCommit>> factory)
            => _commitResponses.Enqueue(factory);

        public void EnqueuePullRequestsResponse(Func<IAsyncEnumerable<GitHubPullRequest>> factory)
            => _prResponses.Enqueue(factory);

        public IAsyncEnumerable<GitHubCommit> GetCommitsAsync(
            GitHubRepository repository, DateTimeOffset since, bool includeStats,
            CancellationToken cancellationToken = default)
            => _commitResponses.Count > 0 ? _commitResponses.Dequeue()() : EmptyCommits();

        public IAsyncEnumerable<GitHubPullRequest> GetPullRequestsAsync(
            GitHubRepository repository, DateTimeOffset since,
            CancellationToken cancellationToken = default)
            => _prResponses.Count > 0 ? _prResponses.Dequeue()() : EmptyPrs();

        private static async IAsyncEnumerable<GitHubCommit> EmptyCommits()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static async IAsyncEnumerable<GitHubPullRequest> EmptyPrs()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}