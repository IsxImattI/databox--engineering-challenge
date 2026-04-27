using DataboxConnector.Core.Models;
using DataboxConnector.Sources.GitHub.Models;

namespace DataboxConnector.Sources.GitHub.Mapping;

/// <summary>
/// Maps <see cref="GitHubCommit"/> API objects to <see cref="RawRecord"/>
/// instances conforming to <see cref="Schema.GitHubCommitsSchema"/>.
/// </summary>
internal static class CommitMapper
{
    /// <summary>
    /// Maps one commit. Returns <c>null</c> if the commit lacks the minimum
    /// required fields (e.g. no SHA), so callers can skip it without aborting.
    /// </summary>
    public static RawRecord? Map(GitHubCommit commit, string repoFullName)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);

        if (string.IsNullOrEmpty(commit.Sha))
            return null;

        var author = commit.Commit?.Author;
        var stats = commit.Stats;
        var parents = commit.Parents ?? new List<GitHubCommitParent>();

        if (author?.Name is null || author.Date is null)
            return null;

        var fields = new Dictionary<string, object?>
        {
            ["sha"]           = commit.Sha,
            ["repo"]          = repoFullName,
            ["author_login"] = commit.Author?.Login, // nullable
            ["author_name"]  = author.Name,
            ["committed_at"]  = author.Date.Value.ToUniversalTime(),
            ["additions"]     = stats?.Additions ?? 0,
            ["deletions"]     = stats?.Deletions ?? 0,
            ["total_changes"] = stats?.Total ?? 0,
            ["is_merge"]      = parents.Count > 1
        };

        return RawRecord.From(fields);
    }
}