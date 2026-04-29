using System.Text.Json.Serialization;

namespace DataboxConnector.Sinks.Databox.Models;

/// <summary>
/// Response payload for <c>POST /v1/data-sources</c>.
/// </summary>
internal sealed class DataboxDataSourceResponse
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("id")]
    [JsonConverter(typeof(Internal.JsonNumberOrStringToStringConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}