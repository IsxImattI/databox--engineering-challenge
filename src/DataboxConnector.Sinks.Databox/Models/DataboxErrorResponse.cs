using System.Text.Json.Serialization;

namespace DataboxConnector.Sinks.Databox.Models;

/// <summary>
/// Standard error envelope returned by the Databox API on 4xx/5xx responses.
/// </summary>
internal sealed class DataboxErrorResponse
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("errors")]
    public List<DataboxErrorItem>? Errors { get; set; }
}

internal sealed class DataboxErrorItem
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}