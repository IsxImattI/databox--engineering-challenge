namespace DataboxConnector.Core.Exceptions;

/// <summary>
/// Base exception for all connector-related failures.
/// </summary>
/// <remarks>
/// Catching this type catches everything thrown by the connector framework,
/// while still allowing more specific subtypes to be caught individually.
/// </remarks>
public class ConnectorException : Exception
{
    public ConnectorException(string message) : base(message) { }
    public ConnectorException(string message, Exception innerException)
        : base(message, innerException) { }
}