using DataboxConnector.Core.Models;
using DataboxConnector.Sources.GitHub.Models;

namespace DataboxConnector.Sources.GitHub.Mapping;

/// <summary>
/// Maps <see cref="GitHubPullRequest"/> API objects to <see cref="RawRecord"/>
/// instances conforming to <see cref="Schema.GitHubPullRequestsSchema"/>.
/// </summary>
internal static class PullRequestMapper
{
    public static RawRecord? Map(GitHubPullRequest pr, string repoFullName)
    {
        ArgumentNullException.ThrowIfNull(pr);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);

        if (pr.Number == 0 || pr.State is null || pr.Title is null)
            return null;

        var isMerged = pr.MergedAt.HasValue;
        var effectiveState = isMerged ? "merged" : pr.State;

        decimal? timeToMergeHours = null;
        if (isMerged)
        {
            var span = pr.MergedAt!.Value.ToUniversalTime() - pr.CreatedAt.ToUniversalTime();
            timeToMergeHours = (decimal)span.TotalHours;
        }

        var fields = new Dictionary<string, object?>
        {
            ["pr_id"]               = pr.Number,
            ["repo"]                = repoFullName,
            ["state"]               = effectiveState,
            ["title"]               = pr.Title,
            ["author_login"]       = pr.User?.Login,
            ["created_at"]          = pr.CreatedAt.ToUniversalTime(),
            ["closed_at"]           = pr.ClosedAt?.ToUniversalTime(),
            ["merged_at"]           = pr.MergedAt?.ToUniversalTime(),
            ["is_merged"]           = isMerged,
            ["time_to_merge_hours"] = timeToMergeHours
        };

        return RawRecord.From(fields);
    }
}