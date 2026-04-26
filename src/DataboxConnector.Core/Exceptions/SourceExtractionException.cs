namespace DataboxConnector.Core.Exceptions;

/// <summary>
/// Thrown when a source connector fails during data extraction.
/// </summary>
public sealed class SourceExtractionException : ConnectorException
{
    public string SourceName { get; }

    public SourceExtractionException(string sourceName, string message)
        : base($"Source '{sourceName}' failed during extraction: {message}")
    {
        SourceName = sourceName;
    }

    public SourceExtractionException(string sourceName, string message, Exception inner)
        : base($"Source '{sourceName}' failed during extraction: {message}", inner)
    {
        SourceName = sourceName;
    }
}