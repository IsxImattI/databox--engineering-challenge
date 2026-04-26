using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;

namespace DataboxConnector.Core.Schema;

/// <summary>
/// Validates raw records against their declared schema.
/// </summary>
/// <remarks>
/// Validation is intentionally strict: any field present in the record must
/// be declared in the schema, and every non-nullable schema field must be
/// present (and non-null) in the record.
/// </remarks>
public static class SchemaValidator
{
    /// <summary>
    /// Validates a single record. Throws on the first violation encountered.
    /// </summary>
    /// <exception cref="SchemaValidationException">
    /// Thrown if the record violates the schema.
    /// </exception>
    public static void Validate(DatasetSchema schema, RawRecord record)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(record);

        // 1. Every record field must exist in the schema
        foreach (var fieldName in record.Fields.Keys)
        {
            if (schema.GetField(fieldName) is null)
                throw new SchemaValidationException(
                    fieldName,
                    $"Field is not declared in schema '{schema.Key}'.");
        }

        // 2. Every non-nullable schema field must be present and non-null
        foreach (var field in schema.Fields)
        {
            var present = record.Fields.TryGetValue(field.Name, out var value);

            if (!field.IsNullable)
            {
                if (!present)
                    throw new SchemaValidationException(
                        field.Name,
                        "Required field is missing.");

                if (value is null)
                    throw new SchemaValidationException(
                        field.Name,
                        "Required field is null.");
            }

            if (value is not null)
                ValidateValueType(field, value);
        }
    }

    private static void ValidateValueType(FieldDefinition field, object value)
    {
        var ok = field.Type switch
        {
            FieldType.String   => value is string,
            FieldType.Integer  => value is int or long or short or byte,
            FieldType.Decimal  => value is decimal or double or float,
            FieldType.Boolean  => value is bool,
            FieldType.DateTime => value is DateTime or DateTimeOffset,
            _                  => false
        };

        if (!ok)
            throw new SchemaValidationException(
                field.Name,
                $"Expected {field.Type} but got {value.GetType().Name}.");
    }
}