using System.Text.Json.Serialization;

namespace DataboxConnector.Sinks.Databox.Models;

/// <summary>
/// Response payload for <c>POST /v1/datasets/{datasetId}/data</c>.
/// </summary>
internal sealed class DataboxIngestionResponse
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("ingestionId")]
    [JsonConverter(typeof(Internal.JsonNumberOrStringToStringConverter))]
    public string? IngestionId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}