using DataboxConnector.Sources.GitHub.Mapping;
using DataboxConnector.Sources.GitHub.Models;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Sources.GitHub.Tests.Mapping;

public class PullRequestMapperTests
{
    private static GitHubPullRequest ValidPr(
        string state = "closed",
        DateTime? closedAt = null,
        DateTime? mergedAt = null) => new()
    {
        Number = 42,
        State = state,
        Title = "Add feature X",
        User = new GitHubUser { Login = "matt" },
        CreatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
        ClosedAt = closedAt,
        MergedAt = mergedAt
    };

    [Fact]
    public void Map_OpenPr_StateOpen_NotMerged()
    {
        var record = PullRequestMapper.Map(ValidPr(state: "open"), "o/r");

        record.Should().NotBeNull();
        record!.Fields["state"].Should().Be("open");
        record.Fields["is_merged"].Should().Be(false);
        record.Fields["time_to_merge_hours"].Should().BeNull();
        record.Fields["closed_at"].Should().BeNull();
        record.Fields["merged_at"].Should().BeNull();
    }

    [Fact]
    public void Map_ClosedNotMerged_StateClosed_NotMerged()
    {
        var pr = ValidPr(
            state: "closed",
            closedAt: new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc));

        var record = PullRequestMapper.Map(pr, "o/r");

        record!.Fields["state"].Should().Be("closed");
        record.Fields["is_merged"].Should().Be(false);
        record.Fields["closed_at"].Should().NotBeNull();
        record.Fields["merged_at"].Should().BeNull();
        record.Fields["time_to_merge_hours"].Should().BeNull();
    }

    [Fact]
    public void Map_MergedPr_StateMerged_TimeCalculated()
    {
        // Created at 10:00 UTC, merged at 14:30 UTC → 4.5 hours
        var pr = ValidPr(
            state: "closed",
            closedAt: new DateTime(2026, 4, 1, 14, 30, 0, DateTimeKind.Utc),
            mergedAt: new DateTime(2026, 4, 1, 14, 30, 0, DateTimeKind.Utc));

        var record = PullRequestMapper.Map(pr, "o/r");

        record!.Fields["state"].Should().Be("merged");
        record.Fields["is_merged"].Should().Be(true);
        record.Fields["time_to_merge_hours"].Should().Be(4.5m);
    }

    [Fact]
    public void Map_PrIdAndRepoSet()
    {
        var record = PullRequestMapper.Map(ValidPr(), "owner/myrepo");

        record!.Fields["pr_id"].Should().Be(42);
        record.Fields["repo"].Should().Be("owner/myrepo");
    }

    [Fact]
    public void Map_NoUser_AuthorLoginNull()
    {
        var pr = ValidPr();
        pr.User = null;

        var record = PullRequestMapper.Map(pr, "o/r");

        record!.Fields["author_login"].Should().BeNull();
    }

    [Fact]
    public void Map_ZeroNumber_ReturnsNull()
    {
        var pr = ValidPr();
        pr.Number = 0;

        PullRequestMapper.Map(pr, "o/r").Should().BeNull();
    }

    [Fact]
    public void Map_StateNull_ReturnsNull()
    {
        var pr = ValidPr();
        pr.State = null;

        PullRequestMapper.Map(pr, "o/r").Should().BeNull();
    }

    [Fact]
    public void Map_TitleNull_ReturnsNull()
    {
        var pr = ValidPr();
        pr.Title = null;

        PullRequestMapper.Map(pr, "o/r").Should().BeNull();
    }

    [Fact]
    public void Map_DatesNormalizedToUtc()
    {
        var pr = ValidPr(
            mergedAt: new DateTime(2026, 4, 1, 16, 0, 0, DateTimeKind.Local));

        var record = PullRequestMapper.Map(pr, "o/r");

        record.Should().NotBeNull();
        ((DateTime)record!.Fields["created_at"]!).Kind.Should().Be(DateTimeKind.Utc);
        ((DateTime)record.Fields["merged_at"]!).Kind.Should().Be(DateTimeKind.Utc);
    }
}