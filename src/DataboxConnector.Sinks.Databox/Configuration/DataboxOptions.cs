using System.ComponentModel.DataAnnotations;

namespace DataboxConnector.Sinks.Databox.Configuration;

/// <summary>
/// Configuration for the Databox Ingestion API client.
/// </summary>
/// <remarks>
/// Bound from the <c>Databox</c> configuration section. Validated on startup
/// via <see cref="ValidateDataAnnotationsAttribute"/> to fail fast if misconfigured.
/// </remarks>
public sealed class DataboxOptions
{
    public const string SectionName = "Databox";

    /// <summary>
    /// Base URL of the Databox API. Defaults to the public production endpoint.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://api.databox.com";

    /// <summary>
    /// API key issued from the Databox profile page.
    /// </summary>
    /// <remarks>
    /// Stored locally outside source control (User Secrets, environment variable,
    /// or local appsettings). Never commit this value.
    /// </remarks>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the data source created in Databox if it does not already exist.
    /// </summary>
    [Required]
    public string DataSourceTitle { get; set; } = "Databox Connector";

    /// <summary>
    /// Path to the local JSON file used to cache provisioned data source / dataset IDs.
    /// Relative paths are resolved against the host's content root.
    /// </summary>
    [Required]
    public string IdentifierStorePath { get; set; } = "data/databox-identifiers.json";
}