using System.Collections.Generic;
using DataboxConnector.Core.Models;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Core.Tests.Models;

public class RawRecordTests
{
    [Fact]
    public void Constructor_NullFields_Throws()
    {
        var act = () => new RawRecord(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void From_CopiesDictionary_SourceMutationDoesNotAffectRecord()
    {
        var source = new Dictionary<string, object?> { ["name"] = "Matt" };

        var record = RawRecord.From(source);
        source["name"] = "Mutated";

        record.Fields["name"].Should().Be("Matt");
    }

    [Fact]
    public void Fields_AreReadOnly()
    {
        var record = RawRecord.From(new Dictionary<string, object?> { ["x"] = 1 });

        record.Fields.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
    }
}