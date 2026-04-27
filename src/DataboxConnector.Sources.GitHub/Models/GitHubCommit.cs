using System.Text.Json.Serialization;

namespace DataboxConnector.Sources.GitHub.Models;

/// <summary>
/// Subset of the GitHub commit object we need for ingestion.
/// </summary>
/// <remarks>
/// Fields we don't use are intentionally not deserialized; the model can be
/// extended later if more attributes need to surface in the dataset.
/// </remarks>
internal sealed class GitHubCommit
{
    [JsonPropertyName("sha")]
    public string? Sha { get; set; }

    [JsonPropertyName("commit")]
    public GitHubCommitMetadata? Commit { get; set; }

    [JsonPropertyName("author")]
    public GitHubUser? Author { get; set; }

    [JsonPropertyName("stats")]
    public GitHubCommitStats? Stats { get; set; }

    [JsonPropertyName("parents")]
    public List<GitHubCommitParent>? Parents { get; set; }
}

internal sealed class GitHubCommitMetadata
{
    [JsonPropertyName("author")]
    public GitHubCommitAuthor? Author { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class GitHubCommitAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }
}

internal sealed class GitHubUser
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

internal sealed class GitHubCommitStats
{
    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

internal sealed class GitHubCommitParent
{
    [JsonPropertyName("sha")]
    public string? Sha { get; set; }
}