using DataboxConnector.Sources.GitHub.Mapping;
using DataboxConnector.Sources.GitHub.Models;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Sources.GitHub.Tests.Mapping;

public class CommitMapperTests
{
    private static GitHubCommit ValidCommit(int parents = 1) => new()
    {
        Sha = "abc123",
        Author = new GitHubUser { Login = "octocat" },
        Commit = new GitHubCommitMetadata
        {
            Author = new GitHubCommitAuthor
            {
                Name = "The Octocat",
                Email = "octo@github.test",
                Date = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc)
            },
            Message = "Fix the bug"
        },
        Stats = new GitHubCommitStats { Additions = 10, Deletions = 5, Total = 15 },
        Parents = Enumerable.Range(0, parents)
            .Select(i => new GitHubCommitParent { Sha = $"p{i}" })
            .ToList()
    };

    [Fact]
    public void Map_ValidCommit_ReturnsRecordWithAllFields()
    {
        var record = CommitMapper.Map(ValidCommit(), "owner/repo");

        record.Should().NotBeNull();
        record!.Fields["sha"].Should().Be("abc123");
        record.Fields["repo"].Should().Be("owner/repo");
        record.Fields["author_login"].Should().Be("octocat");
        record.Fields["author_name"].Should().Be("The Octocat");
        record.Fields["additions"].Should().Be(10);
        record.Fields["deletions"].Should().Be(5);
        record.Fields["total_changes"].Should().Be(15);
        record.Fields["is_merge"].Should().Be(false);
    }

    [Fact]
    public void Map_NoSha_ReturnsNull()
    {
        var commit = ValidCommit();
        commit.Sha = null;

        CommitMapper.Map(commit, "owner/repo").Should().BeNull();
    }

    [Fact]
    public void Map_NoCommitAuthor_ReturnsNull()
    {
        var commit = ValidCommit();
        commit.Commit!.Author = null;

        CommitMapper.Map(commit, "owner/repo").Should().BeNull();
    }

    [Fact]
    public void Map_AuthorNameNull_ReturnsNull()
    {
        var commit = ValidCommit();
        commit.Commit!.Author!.Name = null;

        CommitMapper.Map(commit, "owner/repo").Should().BeNull();
    }

    [Fact]
    public void Map_AuthorDateNull_ReturnsNull()
    {
        var commit = ValidCommit();
        commit.Commit!.Author!.Date = null;

        CommitMapper.Map(commit, "owner/repo").Should().BeNull();
    }

    [Fact]
    public void Map_NoUserAttachment_AuthorLoginNull()
    {
        var commit = ValidCommit();
        commit.Author = null; // No GitHub user attached (commit by external email)

        var record = CommitMapper.Map(commit, "owner/repo");

        record.Should().NotBeNull();
        record!.Fields["author_login"].Should().BeNull();
    }

    [Fact]
    public void Map_NoStats_DefaultsToZero()
    {
        var commit = ValidCommit();
        commit.Stats = null;

        var record = CommitMapper.Map(commit, "owner/repo");

        record.Should().NotBeNull();
        record!.Fields["additions"].Should().Be(0);
        record.Fields["deletions"].Should().Be(0);
        record.Fields["total_changes"].Should().Be(0);
    }

    [Fact]
    public void Map_TwoParents_IsMergeTrue()
    {
        var record = CommitMapper.Map(ValidCommit(parents: 2), "owner/repo");

        record!.Fields["is_merge"].Should().Be(true);
    }

    [Fact]
    public void Map_NoParents_IsMergeFalse()
    {
        var record = CommitMapper.Map(ValidCommit(parents: 0), "owner/repo");

        record!.Fields["is_merge"].Should().Be(false);
    }

    [Fact]
    public void Map_DateNormalizedToUtc()
    {
        var commit = ValidCommit();
        commit.Commit!.Author!.Date = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Local);

        var record = CommitMapper.Map(commit, "owner/repo");

        record.Should().NotBeNull();
        ((DateTime)record!.Fields["committed_at"]!).Kind.Should().Be(DateTimeKind.Utc);
    }
}