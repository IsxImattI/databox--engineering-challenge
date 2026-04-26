namespace DataboxConnector.Core.Schema;

/// <summary>
/// Defines a single column within a <see cref="DatasetSchema"/>.
/// </summary>
public sealed record FieldDefinition
{
    /// <summary>
    /// Field name. Must match the key used in <see cref="Models.RawRecord.Fields"/>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Type of values this field accepts.
    /// </summary>
    public required FieldType Type { get; init; }

    /// <summary>
    /// Whether null values are permitted for this field.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Whether this field is part of the dataset's primary key.
    /// Used by Databox for deduplication.
    /// </summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>
    /// Human-readable description of what this field represents.
    /// Used in generated schema documentation.
    /// </summary>
    public string? Description { get; init; }
}