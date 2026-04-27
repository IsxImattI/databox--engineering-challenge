using DataboxConnector.Core.Schema;

namespace DataboxConnector.Sources.GitHub.Schema;

/// <summary>
/// Schema for the <c>github_pull_requests_v1</c> dataset.
/// </summary>
public static class GitHubPullRequestsSchema
{
    public const string Key = "github_pull_requests_v1";
    public const string Title = "GitHub Pull Requests";

    public static DatasetSchema Instance { get; } = new(
        Key,
        Title,
        new[]
        {
            new FieldDefinition
            {
                Name = "pr_id",
                Type = FieldType.Integer,
                IsPrimaryKey = true,
                Description = "GitHub-issued PR number, unique within a repository."
            },
            new FieldDefinition
            {
                Name = "repo",
                Type = FieldType.String,
                IsPrimaryKey = true,
                Description = "Repository in owner/name form."
            },
            new FieldDefinition
            {
                Name = "state",
                Type = FieldType.String,
                Description = "open, closed, or merged."
            },
            new FieldDefinition
            {
                Name = "title",
                Type = FieldType.String,
                Description = "PR title."
            },
            new FieldDefinition
            {
                Name = "author_login",
                Type = FieldType.String,
                IsNullable = true,
                Description = "GitHub login of the PR author."
            },
            new FieldDefinition
            {
                Name = "created_at",
                Type = FieldType.DateTime,
                Description = "When the PR was opened (UTC)."
            },
            new FieldDefinition
            {
                Name = "closed_at",
                Type = FieldType.DateTime,
                IsNullable = true,
                Description = "When the PR was closed (UTC), if applicable."
            },
            new FieldDefinition
            {
                Name = "merged_at",
                Type = FieldType.DateTime,
                IsNullable = true,
                Description = "When the PR was merged (UTC), if it was merged."
            },
            new FieldDefinition
            {
                Name = "is_merged",
                Type = FieldType.Boolean,
                Description = "Whether the PR was merged into the target branch."
            },
            new FieldDefinition
            {
                Name = "time_to_merge_hours",
                Type = FieldType.Decimal,
                IsNullable = true,
                Description = "Hours between created_at and merged_at; null when not merged."
            }
        });
}