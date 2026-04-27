using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using DataboxConnector.Sinks.Databox;
using DataboxConnector.Sinks.Databox.Configuration;
using DataboxConnector.Sinks.Databox.Internal;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DataboxConnector.Sinks.Databox.Tests;

public class DataboxSinkTests
{
    private static DatasetSchema Schema { get; } = new(
        "test_v1",
        "Test",
        new[]
        {
            new FieldDefinition { Name = "id", Type = FieldType.String }
        });

    private static IOptions<DataboxOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new DataboxOptions
        {
            ApiKey = "k",
            DataSourceTitle = "Test Source"
        });

    private static DataboxSink NewSink(IDataboxApiClient api, IDataboxIdentifierStore store) =>
        new(api, store, Options(), NullLogger<DataboxSink>.Instance);

    // ---------- EnsureReadyAsync ----------

    [Fact]
    public async Task EnsureReadyAsync_BothCached_NoApiCalls()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();

        store.GetDataSourceIdAsync(Arg.Any<CancellationToken>()).Returns("ds-1");
        store.GetDatasetIdAsync(Schema.Key, Arg.Any<CancellationToken>()).Returns("dataset-1");

        var sink = NewSink(api, store);
        await sink.EnsureReadyAsync(Schema);

        await api.DidNotReceive().CreateDataSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().CreateDatasetAsync(
            Arg.Any<string>(), Arg.Any<DatasetSchema>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureReadyAsync_NoDataSource_CreatesAndPersistsBoth()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();

        store.GetDataSourceIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        store.GetDatasetIdAsync(Schema.Key, Arg.Any<CancellationToken>()).Returns((string?)null);
        api.CreateDataSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ds-new");
        api.CreateDatasetAsync(
            Arg.Any<string>(), Arg.Any<DatasetSchema>(), Arg.Any<CancellationToken>()).Returns("dataset-new");

        var sink = NewSink(api, store);
        await sink.EnsureReadyAsync(Schema);

        await api.Received(1).CreateDataSourceAsync("Test Source", Arg.Any<CancellationToken>());
        await api.Received(1).CreateDatasetAsync("ds-new", Schema, Arg.Any<CancellationToken>());

        await store.Received(1).SetDataSourceIdAsync("ds-new", Arg.Any<CancellationToken>());
        await store.Received(1).SetDatasetIdAsync(Schema.Key, "dataset-new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureReadyAsync_DataSourceCachedButDatasetMissing_CreatesOnlyDataset()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();

        store.GetDataSourceIdAsync(Arg.Any<CancellationToken>()).Returns("ds-existing");
        store.GetDatasetIdAsync(Schema.Key, Arg.Any<CancellationToken>()).Returns((string?)null);
        api.CreateDatasetAsync(
            Arg.Any<string>(), Arg.Any<DatasetSchema>(), Arg.Any<CancellationToken>()).Returns("dataset-new");

        var sink = NewSink(api, store);
        await sink.EnsureReadyAsync(Schema);

        await api.DidNotReceive().CreateDataSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await api.Received(1).CreateDatasetAsync("ds-existing", Schema, Arg.Any<CancellationToken>());
    }

    // ---------- SendAsync ----------

    [Fact]
    public async Task SendAsync_EmptyBatch_ReturnsZero()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();

        var sink = NewSink(api, store);
        var sent = await sink.SendAsync(Schema, Array.Empty<RawRecord>());

        sent.Should().Be(0);
        await api.DidNotReceive().IngestRecordsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<RawRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_BatchTooLarge_Throws()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();
        store.GetDatasetIdAsync(Schema.Key, Arg.Any<CancellationToken>()).Returns("did");

        var sink = NewSink(api, store);
        var batch = Enumerable.Range(0, 101)
            .Select(i => RawRecord.From(new Dictionary<string, object?> { ["id"] = $"{i}" }))
            .ToList();

        var act = async () => await sink.SendAsync(Schema, batch);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*100*");
    }

    [Fact]
    public async Task SendAsync_DatasetNotProvisioned_Throws()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();
        store.GetDatasetIdAsync(Schema.Key, Arg.Any<CancellationToken>()).Returns((string?)null);

        var sink = NewSink(api, store);
        var record = RawRecord.From(new Dictionary<string, object?> { ["id"] = "1" });

        var act = async () => await sink.SendAsync(Schema, new[] { record });

        await act.Should().ThrowAsync<SinkIngestionException>()
            .WithMessage("*not provisioned*");
    }

    [Fact]
    public async Task SendAsync_HappyPath_CallsIngestAndReturnsCount()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();
        store.GetDatasetIdAsync(Schema.Key, Arg.Any<CancellationToken>()).Returns("did-x");
        api.IngestRecordsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RawRecord>>(), Arg.Any<CancellationToken>())
           .Returns("ing-1");

        var sink = NewSink(api, store);
        var batch = new[]
        {
            RawRecord.From(new Dictionary<string, object?> { ["id"] = "a" }),
            RawRecord.From(new Dictionary<string, object?> { ["id"] = "b" })
        };

        var sent = await sink.SendAsync(Schema, batch);

        sent.Should().Be(2);
        await api.Received(1).IngestRecordsAsync("did-x", batch, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SinkName_IsLowercase()
    {
        var api = Substitute.For<IDataboxApiClient>();
        var store = Substitute.For<IDataboxIdentifierStore>();

        var sink = NewSink(api, store);
        sink.SinkName.Should().Be("databox");
    }
}