using DataboxConnector.Core.Schema;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Core.Tests.Schema;

public class DatasetSchemaTests
{
    private static FieldDefinition Field(string name, FieldType type = FieldType.String, bool isPk = false) =>
        new() { Name = name, Type = type, IsPrimaryKey = isPk };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_InvalidKey_Throws(string? key)
    {
        var act = () => new DatasetSchema(key!, "Title", new[] { Field("id") });
        act.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_InvalidTitle_Throws(string? title)
    {
        var act = () => new DatasetSchema("key", title!, new[] { Field("id") });
        act.Should().Throw<ArgumentException>().WithParameterName("title");
    }

    [Fact]
    public void Constructor_NullFields_Throws()
    {
        var act = () => new DatasetSchema("key", "Title", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyFields_Throws()
    {
        var act = () => new DatasetSchema("key", "Title", Array.Empty<FieldDefinition>());
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one field*");
    }

    [Fact]
    public void Constructor_DuplicateFieldNames_Throws()
    {
        var fields = new[] { Field("id"), Field("name"), Field("id") };

        var act = () => new DatasetSchema("key", "Title", fields);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate field names*id*");
    }

    [Fact]
    public void Constructor_ValidInput_PopulatesProperties()
    {
        var fields = new[] { Field("id", isPk: true), Field("name") };

        var schema = new DatasetSchema("github_commits_v1", "GitHub Commits", fields);

        schema.Key.Should().Be("github_commits_v1");
        schema.Title.Should().Be("GitHub Commits");
        schema.Fields.Should().HaveCount(2);
        schema.PrimaryKeys.Should().ContainSingle().Which.Should().Be("id");
    }

    [Fact]
    public void Constructor_NoPrimaryKeys_PrimaryKeysIsEmpty()
    {
        var schema = new DatasetSchema("k", "T", new[] { Field("name") });
        schema.PrimaryKeys.Should().BeEmpty();
    }

    [Fact]
    public void GetField_ExistingField_ReturnsField()
    {
        var schema = new DatasetSchema("k", "T", new[] { Field("name") });
        schema.GetField("name").Should().NotBeNull();
    }

    [Fact]
    public void GetField_NonExistentField_ReturnsNull()
    {
        var schema = new DatasetSchema("k", "T", new[] { Field("name") });
        schema.GetField("missing").Should().BeNull();
    }
}