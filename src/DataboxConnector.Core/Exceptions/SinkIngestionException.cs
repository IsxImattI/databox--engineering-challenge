namespace DataboxConnector.Core.Exceptions;

/// <summary>
/// Thrown when a sink fails to deliver records to the downstream system.
/// </summary>
public sealed class SinkIngestionException : ConnectorException
{
    public string SinkName { get; }

    public SinkIngestionException(string sinkName, string message)
        : base($"Sink '{sinkName}' failed during ingestion: {message}")
    {
        SinkName = sinkName;
    }

    public SinkIngestionException(string sinkName, string message, Exception inner)
        : base($"Sink '{sinkName}' failed during ingestion: {message}", inner)
    {
        SinkName = sinkName;
    }
}