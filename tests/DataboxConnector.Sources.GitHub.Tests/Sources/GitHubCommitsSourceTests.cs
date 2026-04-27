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
using NSubstitute;
using Xunit;

namespace DataboxConnector.Sources.GitHub.Tests.Sources;

public class GitHubCommitsSourceTests
{
    private static GitHubCommitsSource NewSource(
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

        return new GitHubCommitsSource(api, options, NullLogger<GitHubCommitsSource>.Instance);
    }

    private static GitHubCommit ValidApiCommit(string sha) => new()
    {
        Sha = sha,
        Author = new GitHubUser { Login = "matt" },
        Commit = new GitHubCommitMetadata
        {
            Author = new GitHubCommitAuthor
            {
                Name = "Matt",
                Date = DateTime.UtcNow
            }
        },
        Parents = new List<GitHubCommitParent> { new() { Sha = "p" } }
    };

    private static async IAsyncEnumerable<GitHubCommit> AsyncOf(
        IEnumerable<GitHubCommit> commits,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var c in commits)
        {
            ct.ThrowIfCancellationRequested();
            yield return c;
            await Task.Yield();
        }
    }

    [Fact]
    public void SourceName_IsGithubCommits()
    {
        var api = Substitute.For<IGitHubApiClient>();
        var source = NewSource(api);

        source.SourceName.Should().Be("github_commits");
    }

    [Fact]
    public void Schema_IsGithubCommitsSchema()
    {
        var api = Substitute.For<IGitHubApiClient>();
        var source = NewSource(api);

        source.Schema.Key.Should().Be("github_commits_v1");
    }

    [Fact]
    public async Task ExtractAsync_StreamsAllCommits()
    {
        var fake = new FakeApiClient();
        fake.EnqueueCommitsResponse(() => AsyncOf(new[] { ValidApiCommit("a"), ValidApiCommit("b") }));

        var source = NewSource(fake);

        var records = new List<RawRecord>();
        await foreach (var r in source.ExtractAsync(new ExtractionContext()))
            records.Add(r);

        records.Should().HaveCount(2);
        records.Select(r => r.Fields["sha"]).Should().Equal("a", "b");
    }

    [Fact]
    public async Task ExtractAsync_MultipleRepos_IteratesEach()
    {
        var fake = new FakeApiClient();
        fake.EnqueueCommitsResponse(() => AsyncOf(new[] { ValidApiCommit("x") }));
        fake.EnqueueCommitsResponse(() => AsyncOf(new[] { ValidApiCommit("y") }));

        var options = new GitHubOptions
        {
            PersonalAccessToken = "t",
            Repositories = new() { "a/x", "b/y" },
            UserAgent = "ua",
            DefaultLookbackDays = 7,
            MaxItemsPerRun = 100
        };
        var source = NewSource(fake, options);

        var count = 0;
        await foreach (var _ in source.ExtractAsync(new ExtractionContext()))
            count++;

        count.Should().Be(2);
        fake.CommitCalls.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExtractAsync_InvalidRepoFormat_Throws()
    {
        var fake = new FakeApiClient();
        var options = new GitHubOptions
        {
            PersonalAccessToken = "t",
            Repositories = new() { "no-slash" },
            UserAgent = "ua",
            DefaultLookbackDays = 7,
            MaxItemsPerRun = 100
        };
        var source = NewSource(fake, options);

        var act = async () =>
        {
            await foreach (var _ in source.ExtractAsync(new ExtractionContext())) { }
        };

        await act.Should().ThrowAsync<SourceExtractionException>()
            .WithMessage("*owner/name*");
    }

    [Fact]
    public async Task ExtractAsync_MaxItemsPerRun_HaltsExtraction()
    {
        var fake = new FakeApiClient();
        fake.EnqueueCommitsResponse(() =>
            AsyncOf(Enumerable.Range(0, 50).Select(i => ValidApiCommit($"sha{i}"))));

        var options = new GitHubOptions
        {
            PersonalAccessToken = "t",
            Repositories = new() { "a/x" },
            UserAgent = "ua",
            DefaultLookbackDays = 7,
            MaxItemsPerRun = 5
        };
        var source = NewSource(fake, options);

        var count = 0;
        await foreach (var _ in source.ExtractAsync(new ExtractionContext()))
            count++;

        count.Should().Be(5);
    }

    [Fact]
    public async Task ExtractAsync_UsesContextFromIfProvided()
    {
        var fake = new FakeApiClient();
        fake.EnqueueCommitsResponse(() => AsyncOf(Array.Empty<GitHubCommit>()));

        var source = NewSource(fake);
        var since = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await foreach (var _ in source.ExtractAsync(new ExtractionContext { From = since })) { }

        fake.CommitCalls.Should().ContainSingle()
            .Which.since.Should().Be(since);
    }

    /// <summary>
    /// Test fake for IGitHubApiClient that returns predefined async streams.
    /// </summary>
    /// <remarks>
    /// Avoids NSubstitute's quirky handling of IAsyncEnumerable return values
    /// (compiler treats Returns&lt;IAsyncEnumerable&lt;T&gt;&gt; as awaitable
    /// and looks for a non-existent GetAwaiter).
    /// </remarks>
    private sealed class FakeApiClient : IGitHubApiClient
    {
        private readonly Queue<Func<IAsyncEnumerable<GitHubCommit>>> _commitResponses = new();
        private readonly Queue<Func<IAsyncEnumerable<GitHubPullRequest>>> _prResponses = new();

        public List<(GitHubRepository repo, DateTimeOffset since, bool stats)> CommitCalls { get; } = new();
        public List<(GitHubRepository repo, DateTimeOffset since)> PrCalls { get; } = new();

        public void EnqueueCommitsResponse(Func<IAsyncEnumerable<GitHubCommit>> factory)
            => _commitResponses.Enqueue(factory);

        public void EnqueuePullRequestsResponse(Func<IAsyncEnumerable<GitHubPullRequest>> factory)
            => _prResponses.Enqueue(factory);

        public IAsyncEnumerable<GitHubCommit> GetCommitsAsync(
            GitHubRepository repository,
            DateTimeOffset since,
            bool includeStats,
            CancellationToken cancellationToken = default)
        {
            CommitCalls.Add((repository, since, includeStats));
            return _commitResponses.Count > 0
                ? _commitResponses.Dequeue()()
                : EmptyCommits();
        }

        public IAsyncEnumerable<GitHubPullRequest> GetPullRequestsAsync(
            GitHubRepository repository,
            DateTimeOffset since,
            CancellationToken cancellationToken = default)
        {
            PrCalls.Add((repository, since));
            return _prResponses.Count > 0
                ? _prResponses.Dequeue()()
                : EmptyPrs();
        }

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