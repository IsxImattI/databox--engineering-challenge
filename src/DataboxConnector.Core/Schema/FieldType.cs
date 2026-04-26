namespace DataboxConnector.Core.Schema;

/// <summary>
/// Supported field types for dataset schemas.
/// </summary>
/// <remarks>
/// Mapped to JSON-compatible primitives that the Databox Ingestion API accepts.
/// </remarks>
public enum FieldType
{
    String,
    Integer,
    Decimal,
    Boolean,
    DateTime
}