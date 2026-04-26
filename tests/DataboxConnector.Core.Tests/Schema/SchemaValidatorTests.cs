using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Core.Tests.Schema;

public class SchemaValidatorTests
{
    private static DatasetSchema BuildSchema(params FieldDefinition[] fields) =>
        new("test_dataset", "Test Dataset", fields);

    private static FieldDefinition Field(
        string name,
        FieldType type = FieldType.String,
        bool nullable = false) =>
        new() { Name = name, Type = type, IsNullable = nullable };

    [Fact]
    public void Validate_NullSchema_Throws()
    {
        var record = RawRecord.From(new Dictionary<string, object?>());
        var act = () => SchemaValidator.Validate(null!, record);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_NullRecord_Throws()
    {
        var schema = BuildSchema(Field("id"));
        var act = () => SchemaValidator.Validate(schema, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_FieldNotInSchema_Throws()
    {
        var schema = BuildSchema(Field("id"));
        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"]      = "abc",
            ["unknown"] = "x"
        });

        var act = () => SchemaValidator.Validate(schema, record);

        act.Should().Throw<SchemaValidationException>()
            .Where(e => e.FieldName == "unknown");
    }

    [Fact]
    public void Validate_RequiredFieldMissing_Throws()
    {
        var schema = BuildSchema(Field("id"), Field("name"));
        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"] = "abc"
            // "name" missing
        });

        var act = () => SchemaValidator.Validate(schema, record);

        act.Should().Throw<SchemaValidationException>()
            .Where(e => e.FieldName == "name" && e.Reason.Contains("missing"));
    }

    [Fact]
    public void Validate_RequiredFieldNull_Throws()
    {
        var schema = BuildSchema(Field("id"));
        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"] = null
        });

        var act = () => SchemaValidator.Validate(schema, record);

        act.Should().Throw<SchemaValidationException>()
            .Where(e => e.FieldName == "id" && e.Reason.Contains("null"));
    }

    [Fact]
    public void Validate_NullableFieldNull_Passes()
    {
        var schema = BuildSchema(Field("description", nullable: true));
        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["description"] = null
        });

        var act = () => SchemaValidator.Validate(schema, record);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NullableFieldMissing_Passes()
    {
        var schema = BuildSchema(
            Field("id"),
            Field("description", nullable: true));

        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"] = "abc"
        });

        var act = () => SchemaValidator.Validate(schema, record);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData(42, false)]
    [InlineData(true, false)]
    public void Validate_StringField_TypeChecked(object value, bool shouldPass)
    {
        var schema = BuildSchema(Field("text", FieldType.String));
        var record = RawRecord.From(new Dictionary<string, object?> { ["text"] = value });

        var act = () => SchemaValidator.Validate(schema, record);

        if (shouldPass) act.Should().NotThrow();
        else act.Should().Throw<SchemaValidationException>();
    }

    [Theory]
    [InlineData((int)42, true)]
    [InlineData((long)42, true)]
    [InlineData((short)42, true)]
    [InlineData((byte)42, true)]
    [InlineData("42", false)]
    [InlineData(42.5, false)]
    public void Validate_IntegerField_AcceptsIntegralTypes(object value, bool shouldPass)
    {
        var schema = BuildSchema(Field("count", FieldType.Integer));
        var record = RawRecord.From(new Dictionary<string, object?> { ["count"] = value });

        var act = () => SchemaValidator.Validate(schema, record);

        if (shouldPass) act.Should().NotThrow();
        else act.Should().Throw<SchemaValidationException>();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(0, false)]
    [InlineData("true", false)]
    public void Validate_BooleanField_OnlyAcceptsBool(object value, bool shouldPass)
    {
        var schema = BuildSchema(Field("active", FieldType.Boolean));
        var record = RawRecord.From(new Dictionary<string, object?> { ["active"] = value });

        var act = () => SchemaValidator.Validate(schema, record);

        if (shouldPass) act.Should().NotThrow();
        else act.Should().Throw<SchemaValidationException>();
    }

    [Fact]
    public void Validate_DateTimeField_AcceptsDateTimeAndDateTimeOffset()
    {
        var schema = BuildSchema(Field("when", FieldType.DateTime));

        var dt  = RawRecord.From(new Dictionary<string, object?> { ["when"] = DateTime.UtcNow });
        var dto = RawRecord.From(new Dictionary<string, object?> { ["when"] = DateTimeOffset.UtcNow });

        ((Action)(() => SchemaValidator.Validate(schema, dt))).Should().NotThrow();
        ((Action)(() => SchemaValidator.Validate(schema, dto))).Should().NotThrow();
    }

    [Fact]
    public void Validate_DecimalField_AcceptsDecimalDoubleFloat()
    {
        var schema = BuildSchema(Field("price", FieldType.Decimal));

        foreach (object value in new object[] { 1.5m, 1.5d, 1.5f })
        {
            var record = RawRecord.From(new Dictionary<string, object?> { ["price"] = value });
            ((Action)(() => SchemaValidator.Validate(schema, record))).Should().NotThrow();
        }
    }

    [Fact]
    public void Validate_FullValidRecord_Passes()
    {
        var schema = BuildSchema(
            Field("id", FieldType.String),
            Field("count", FieldType.Integer),
            Field("active", FieldType.Boolean),
            Field("created", FieldType.DateTime),
            Field("note", FieldType.String, nullable: true));

        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"]      = "abc",
            ["count"]   = 42,
            ["active"]  = true,
            ["created"] = DateTime.UtcNow,
            ["note"]    = null
        });

        var act = () => SchemaValidator.Validate(schema, record);
        act.Should().NotThrow();
    }
}