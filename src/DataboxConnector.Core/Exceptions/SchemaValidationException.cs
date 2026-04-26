namespace DataboxConnector.Core.Exceptions;

/// <summary>
/// Thrown when a record fails validation against its declared schema.
/// </summary>
public sealed class SchemaValidationException : ConnectorException
{
    public string FieldName { get; }
    public string Reason { get; }

    public SchemaValidationException(string fieldName, string reason)
        : base($"Schema validation failed for field '{fieldName}': {reason}")
    {
        FieldName = fieldName;
        Reason = reason;
    }
}