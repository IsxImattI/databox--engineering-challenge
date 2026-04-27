using System.Net;
using System.Net.Http;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.GitHub.Internal;
using DataboxConnector.Sources.GitHub.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Xunit;

namespace DataboxConnector.Sources.GitHub.Tests.Internal;

public class GitHubApiClientTests
{
    private static readonly Uri BaseAddress = new("https://api.github.test");

    private static (GitHubApiClient client, MockHttpMessageHandler mock) NewClient()
    {
        var mock = new MockHttpMessageHandler();
        var http = new HttpClient(mock) { BaseAddress = BaseAddress };
        return (new GitHubApiClient(http, NullLogger<GitHubApiClient>.Instance), mock);
    }

    private static GitHubRepository Repo => new("octo", "repo");
    private static DateTimeOffset Since => new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------- GetCommitsAsync ----------

    [Fact]
    public async Task GetCommitsAsync_SinglePage_YieldsAllCommits()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/commits*")
            .Respond("application/json", """
                [
                    { "sha": "a", "commit": {"author": {"name": "A", "date": "2026-04-02T00:00:00Z"}}, "parents": [] },
                    { "sha": "b", "commit": {"author": {"name": "B", "date": "2026-04-02T00:00:00Z"}}, "parents": [] }
                ]
                """);

        var commits = new List<GitHubCommit>();
        await foreach (var c in client.GetCommitsAsync(Repo, Since, includeStats: false))
            commits.Add(c);

        commits.Should().HaveCount(2);
        commits.Select(c => c.Sha).Should().Equal("a", "b");
    }

    [Fact]
    public async Task GetCommitsAsync_TwoPages_FollowsLinkHeader()
    {
        var (client, mock) = NewClient();
        var page2Url = $"{BaseAddress}repos/octo/repo/commits?page=2";

        // Page 1
        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/commits")
            .WithQueryString("since", "2026-04-01T00:00:00Z")
            .Respond(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{ "sha": "a", "commit": {"author": {"name": "A", "date": "2026-04-02T00:00:00Z"}}, "parents": [] }]""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
                Headers = { { "Link", $"<{page2Url}>; rel=\"next\"" } }
            });

        // Page 2
        mock.When(HttpMethod.Get, page2Url)
            .Respond("application/json",
                """[{ "sha": "b", "commit": {"author": {"name": "B", "date": "2026-04-02T00:00:00Z"}}, "parents": [] }]""");

        var commits = new List<GitHubCommit>();
        await foreach (var c in client.GetCommitsAsync(Repo, Since, includeStats: false))
            commits.Add(c);

        commits.Should().HaveCount(2);
        commits.Select(c => c.Sha).Should().Equal("a", "b");
    }

    [Fact]
    public async Task GetCommitsAsync_IncludeStats_FetchesDetailPerCommit()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/commits")
            .WithQueryString("since", "2026-04-01T00:00:00Z")
            .Respond("application/json", """
                [{ "sha": "a", "commit": {"author": {"name": "A", "date": "2026-04-02T00:00:00Z"}}, "parents": [] }]
                """);

        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/commits/a")
            .Respond("application/json", """
                {
                    "sha": "a",
                    "commit": {"author": {"name": "A", "date": "2026-04-02T00:00:00Z"}},
                    "parents": [],
                    "stats": { "additions": 10, "deletions": 2, "total": 12 }
                }
                """);

        var commits = new List<GitHubCommit>();
        await foreach (var c in client.GetCommitsAsync(Repo, Since, includeStats: true))
            commits.Add(c);

        commits.Should().HaveCount(1);
        commits[0].Stats.Should().NotBeNull();
        commits[0].Stats!.Additions.Should().Be(10);
        commits[0].Stats!.Total.Should().Be(12);
    }

    [Fact]
    public async Task GetCommitsAsync_401_ThrowsAuthMessage()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/commits*")
            .Respond(HttpStatusCode.Unauthorized, "application/json", """{"message": "Bad credentials"}""");

        var act = async () =>
        {
            await foreach (var _ in client.GetCommitsAsync(Repo, Since, false)) { }
        };

        var ex = await act.Should().ThrowAsync<SourceExtractionException>();
        ex.Which.Message.Should().Contain("Authentication failed");
    }

    [Fact]
    public async Task GetCommitsAsync_404_ThrowsRepoMessage()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/commits*")
            .Respond(HttpStatusCode.NotFound, "application/json", """{"message": "Not Found"}""");

        var act = async () =>
        {
            await foreach (var _ in client.GetCommitsAsync(Repo, Since, false)) { }
        };

        var ex = await act.Should().ThrowAsync<SourceExtractionException>();
        ex.Which.Message.Should().Contain("404");
    }

    // ---------- GetPullRequestsAsync ----------

    [Fact]
    public async Task GetPullRequestsAsync_StopsAtSinceCutoff()
    {
        var (client, mock) = NewClient();

        // Returns one fresh PR and one stale PR; the stale one should not be yielded.
        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/pulls*")
            .Respond("application/json", """
                [
                    { "number": 100, "state": "open", "title": "Fresh",
                      "created_at": "2026-04-15T00:00:00Z" },
                    { "number": 99, "state": "closed", "title": "Stale",
                      "created_at": "2026-03-15T00:00:00Z" }
                ]
                """);

        var prs = new List<GitHubPullRequest>();
        await foreach (var pr in client.GetPullRequestsAsync(Repo, Since))
            prs.Add(pr);

        prs.Should().HaveCount(1);
        prs[0].Number.Should().Be(100);
    }

    [Fact]
    public async Task GetPullRequestsAsync_AllFresh_YieldsAll()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, BaseAddress + "repos/octo/repo/pulls*")
            .Respond("application/json", """
                [
                    { "number": 1, "state": "open", "title": "A", "created_at": "2026-04-15T00:00:00Z" },
                    { "number": 2, "state": "open", "title": "B", "created_at": "2026-04-10T00:00:00Z" }
                ]
                """);

        var prs = new List<GitHubPullRequest>();
        await foreach (var pr in client.GetPullRequestsAsync(Repo, Since))
            prs.Add(pr);

        prs.Should().HaveCount(2);
    }
}