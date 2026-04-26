using System.Collections.Generic;
using System.Linq;

namespace DataboxConnector.Core.Schema;

/// <summary>
/// Describes the shape of a dataset that flows through the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The schema is treated as a first-class artifact: it is defined alongside each
/// source, can be serialized to documentation, and is used to validate raw records
/// before they are sent to a sink.
/// </para>
/// <para>
/// Schemas should be versioned via the <see cref="Key"/> (e.g. <c>github_commits_v1</c>)
/// so that breaking changes can be introduced as new datasets without affecting
/// existing dashboards.
/// </para>
/// </remarks>
public sealed class DatasetSchema
{
    /// <summary>
    /// Stable identifier used as the Databox dataset key.
    /// Should be lowercase snake_case with a version suffix (e.g. <c>github_commits_v1</c>).
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Human-readable title shown in the Databox UI.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// All fields in the dataset, in declaration order.
    /// </summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>
    /// Names of fields that together form the primary key.
    /// Empty if the dataset has no natural unique identifier.
    /// </summary>
    public IReadOnlyList<string> PrimaryKeys { get; }

    public DatasetSchema(string key, string title, IReadOnlyList<FieldDefinition> fields)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Schema key must not be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Schema title must not be empty.", nameof(title));
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
            throw new ArgumentException("Schema must declare at least one field.", nameof(fields));

        var duplicates = fields
            .GroupBy(f => f.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new ArgumentException(
                $"Duplicate field names found: {string.Join(", ", duplicates)}",
                nameof(fields));

        Key = key;
        Title = title;
        Fields = fields;
        PrimaryKeys = fields.Where(f => f.IsPrimaryKey).Select(f => f.Name).ToList();
    }

    /// <summary>
    /// Returns the field with the given name, or <c>null</c> if it doesn't exist.
    /// </summary>
    public FieldDefinition? GetField(string name)
        => Fields.FirstOrDefault(f => f.Name == name);
}