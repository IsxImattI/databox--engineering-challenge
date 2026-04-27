using System.ComponentModel.DataAnnotations;

namespace DataboxConnector.Sources.GitHub.Configuration;

/// <summary>
/// Configuration for the GitHub source.
/// </summary>
/// <remarks>
/// Bound from the <c>Sources:GitHub</c> configuration section. The PAT must be
/// stored outside source control (User Secrets, environment variable, or a
/// local appsettings excluded from git).
/// </remarks>
public sealed class GitHubOptions
{
    public const string SectionName = "Sources:GitHub";

    /// <summary>
    /// Base URL of the GitHub REST API. Defaults to public GitHub; can be
    /// pointed at GitHub Enterprise instances if needed.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// Personal Access Token used for authentication.
    /// </summary>
    /// <remarks>
    /// A classic PAT with <c>public_repo</c> scope is sufficient for public repos.
    /// For private repos, a fine-grained PAT with <c>contents:read</c> and
    /// <c>pull-requests:read</c> is recommended.
    /// </remarks>
    [Required]
    public string PersonalAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Repositories to extract data from, in <c>owner/name</c> format.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<string> Repositories { get; set; } = new();

    /// <summary>
    /// User agent header value sent with every request.
    /// GitHub requires a non-empty User-Agent.
    /// </summary>
    [Required]
    public string UserAgent { get; set; } = "DataboxConnector/1.0";

    /// <summary>
    /// Default number of days to look back when no <c>From</c> is supplied
    /// in the extraction context.
    /// </summary>
    [Range(1, 365)]
    public int DefaultLookbackDays { get; set; } = 30;

    /// <summary>
    /// Maximum number of items to extract per source run.
    /// Safety cap to prevent runaway extraction in case of misconfiguration.
    /// </summary>
    [Range(1, 100_000)]
    public int MaxItemsPerRun { get; set; } = 5_000;

    /// <summary>
    /// When <c>true</c>, fetches per-commit statistics (additions, deletions, total)
    /// via an additional <c>GET /commits/{sha}</c> request for each commit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disabled by default because it consumes ~100x more rate-limit budget
    /// (GitHub allows 5000 authenticated requests per hour for classic PATs).
    /// </para>
    /// <para>
    /// When disabled, the <c>additions</c>, <c>deletions</c>, and <c>total_changes</c>
    /// fields in <c>github_commits_v1</c> will be reported as 0 — useful for commit
    /// frequency metrics but not for code-churn analysis.
    /// </para>
    /// </remarks>
    public bool IncludeCommitStats { get; set; } = false;
}