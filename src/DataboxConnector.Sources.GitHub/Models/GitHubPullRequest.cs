using System.Text.Json.Serialization;

namespace DataboxConnector.Sources.GitHub.Models;

/// <summary>
/// Subset of the GitHub pull request object we need for ingestion.
/// </summary>
internal sealed class GitHubPullRequest
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("user")]
    public GitHubUser? User { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [JsonPropertyName("merged_at")]
    public DateTime? MergedAt { get; set; }
}