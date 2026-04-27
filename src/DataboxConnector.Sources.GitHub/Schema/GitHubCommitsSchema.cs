using DataboxConnector.Core.Schema;

namespace DataboxConnector.Sources.GitHub.Schema;

/// <summary>
/// Schema for the <c>github_commits_v1</c> dataset.
/// </summary>
/// <remarks>
/// Versioned key (<c>_v1</c>) reserves the option to evolve the schema as a
/// new dataset later without disturbing existing dashboards.
/// </remarks>
public static class GitHubCommitsSchema
{
    public const string Key = "github_commits_v1";
    public const string Title = "GitHub Commits";

    public static DatasetSchema Instance { get; } = new(
        Key,
        Title,
        new[]
        {
            new FieldDefinition
            {
                Name = "sha",
                Type = FieldType.String,
                IsPrimaryKey = true,
                Description = "Commit SHA-1 hash."
            },
            new FieldDefinition
            {
                Name = "repo",
                Type = FieldType.String,
                Description = "Repository in owner/name form."
            },
            new FieldDefinition
            {
                Name = "author_login",
                Type = FieldType.String,
                IsNullable = true,
                Description = "GitHub login of the commit author, if known."
            },
            new FieldDefinition
            {
                Name = "author_name",
                Type = FieldType.String,
                Description = "Display name from the commit metadata."
            },
            new FieldDefinition
            {
                Name = "committed_at",
                Type = FieldType.DateTime,
                Description = "When the commit was authored (UTC)."
            },
            new FieldDefinition
            {
                Name = "additions",
                Type = FieldType.Integer,
                Description = "Lines added in this commit."
            },
            new FieldDefinition
            {
                Name = "deletions",
                Type = FieldType.Integer,
                Description = "Lines deleted in this commit."
            },
            new FieldDefinition
            {
                Name = "total_changes",
                Type = FieldType.Integer,
                Description = "Sum of additions and deletions."
            },
            new FieldDefinition
            {
                Name = "is_merge",
                Type = FieldType.Boolean,
                Description = "True when the commit has more than one parent."
            }
        });
}