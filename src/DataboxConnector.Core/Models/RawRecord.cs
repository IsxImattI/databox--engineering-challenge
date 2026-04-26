using System.Collections.Generic;

namespace DataboxConnector.Core.Models;

/// <summary>
/// A single row of raw data extracted from a source.
/// Field names map to the columns defined in <see cref="Schema.DatasetSchema"/>.
/// </summary>
/// <remarks>
/// <para>
/// We use a dictionary instead of a strongly typed model because each source produces
/// a different shape of data. Keeping the contract loose at the pipeline boundary
/// allows new sources to be added without modifying core abstractions.
/// </para>
/// <para>
/// Type safety is enforced at the boundary by validating each record against
/// its <see cref="Schema.DatasetSchema"/> before it leaves the pipeline.
/// </para>
/// </remarks>
public sealed class RawRecord
{
    /// <summary>
    /// The actual data fields. Keys are field names; values are the raw values
    /// (string, int, decimal, bool, DateTime, or null).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields { get; }

    public RawRecord(IReadOnlyDictionary<string, object?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields;
    }

    /// <summary>
    /// Convenience factory for constructing records from a dictionary literal.
    /// </summary>
    public static RawRecord From(IDictionary<string, object?> fields)
        => new(new Dictionary<string, object?>(fields));
}