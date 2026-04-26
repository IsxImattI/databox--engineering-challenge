using System.Runtime.CompilerServices;
using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Pipeline;
using DataboxConnector.Core.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DataboxConnector.Core.Tests.Pipeline;

public class IngestionPipelineTests
{
    private static DatasetSchema MinimalSchema { get; } = new(
        "test_v1",
        "Test",
        new[]
        {
            new FieldDefinition { Name = "id",   Type = FieldType.String, IsPrimaryKey = true },
            new FieldDefinition { Name = "name", Type = FieldType.String }
        });

    private static RawRecord ValidRecord(string id = "1", string name = "n") =>
        RawRecord.From(new Dictionary<string, object?> { ["id"] = id, ["name"] = name });

    private static IngestionPipeline NewPipeline(int batchSize = 100) =>
        new(NullLogger<IngestionPipeline>.Instance, batchSize);

    private static ISourceConnector MockSource(IAsyncEnumerable<RawRecord> records)
    {
        var src = Substitute.For<ISourceConnector>();
        src.SourceName.Returns("test-source");
        src.Schema.Returns(MinimalSchema);
        src.ExtractAsync(Arg.Any<ExtractionContext>(), Arg.Any<CancellationToken>())
           .Returns(records);
        return src;
    }

    private static ISinkConnector MockSink()
    {
        var sink = Substitute.For<ISinkConnector>();
        sink.SinkName.Returns("test-sink");
        sink.SendAsync(Arg.Any<DatasetSchema>(), Arg.Any<IReadOnlyList<RawRecord>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<IReadOnlyList<RawRecord>>().Count));
        return sink;
    }

    private static async IAsyncEnumerable<RawRecord> AsyncRecords(
        IEnumerable<RawRecord> records,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return r;
            await Task.Yield();
        }
    }

    [Fact]
    public void Constructor_BatchSizeZero_Throws()
    {
        var act = () => new IngestionPipeline(NullLogger<IngestionPipeline>.Instance, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_BatchSizeTooLarge_Throws()
    {
        var act = () => new IngestionPipeline(NullLogger<IngestionPipeline>.Instance, 1001);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RunAsync_NullSource_Throws()
    {
        var pipeline = NewPipeline();
        var act = async () => await pipeline.RunAsync(null!, MockSink(), new ExtractionContext());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_NullSink_Throws()
    {
        var pipeline = NewPipeline();
        var src = MockSource(AsyncRecords(Array.Empty<RawRecord>()));
        var act = async () => await pipeline.RunAsync(src, null!, new ExtractionContext());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_EmptySource_ReturnsSuccessWithZeroCounts()
    {
        var pipeline = NewPipeline();
        var src = MockSource(AsyncRecords(Array.Empty<RawRecord>()));
        var sink = MockSink();

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext());

        result.Success.Should().BeTrue();
        result.RecordsExtracted.Should().Be(0);
        result.RecordsSent.Should().Be(0);
        result.BatchesSent.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_FewerRecordsThanBatchSize_OneBatchSent()
    {
        var records = Enumerable.Range(0, 5).Select(i => ValidRecord($"{i}")).ToList();
        var pipeline = NewPipeline(batchSize: 100);
        var src  = MockSource(AsyncRecords(records));
        var sink = MockSink();

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext());

        result.Success.Should().BeTrue();
        result.RecordsExtracted.Should().Be(5);
        result.RecordsSent.Should().Be(5);
        result.BatchesSent.Should().Be(1);

        await sink.Received(1).SendAsync(
            Arg.Is(MinimalSchema),
            Arg.Is<IReadOnlyList<RawRecord>>(b => b.Count == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MoreRecordsThanBatchSize_MultipleBatches()
    {
        var records = Enumerable.Range(0, 250).Select(i => ValidRecord($"{i}")).ToList();
        var pipeline = NewPipeline(batchSize: 100);
        var src  = MockSource(AsyncRecords(records));
        var sink = MockSink();

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext());

        result.Success.Should().BeTrue();
        result.RecordsExtracted.Should().Be(250);
        result.RecordsSent.Should().Be(250);
        result.BatchesSent.Should().Be(3); // 100 + 100 + 50

        await sink.Received(3).SendAsync(
            Arg.Any<DatasetSchema>(),
            Arg.Any<IReadOnlyList<RawRecord>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EnsureReadyCalledOncePerRun()
    {
        var pipeline = NewPipeline();
        var src  = MockSource(AsyncRecords(new[] { ValidRecord() }));
        var sink = MockSink();

        await pipeline.RunAsync(src, sink, new ExtractionContext());

        await sink.Received(1).EnsureReadyAsync(MinimalSchema, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_InvalidRecord_ReturnsFailureResult()
    {
        var bad = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"] = "1"
            // "name" missing
        });

        var pipeline = NewPipeline();
        var src  = MockSource(AsyncRecords(new[] { bad }));
        var sink = MockSink();

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Schema validation failed");
        result.RecordsSent.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_SourceThrowsExtractionException_ReturnsFailureResult()
    {
        var src = Substitute.For<ISourceConnector>();
        src.SourceName.Returns("github");
        src.Schema.Returns(MinimalSchema);
        src.ExtractAsync(Arg.Any<ExtractionContext>(), Arg.Any<CancellationToken>())
           .Returns(_ => Throwing());

        var pipeline = NewPipeline();
        var sink = MockSink();

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("github");

        static async IAsyncEnumerable<RawRecord> Throwing()
        {
            await Task.Yield();
            throw new SourceExtractionException("github", "API down");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public async Task RunAsync_SinkThrowsIngestionException_ReturnsFailureResult()
    {
        var pipeline = NewPipeline();
        var src  = MockSource(AsyncRecords(new[] { ValidRecord() }));
        var sink = MockSink();
        sink.SendAsync(Arg.Any<DatasetSchema>(), Arg.Any<IReadOnlyList<RawRecord>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new SinkIngestionException("databox", "5xx"));

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("databox");
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var pipeline = NewPipeline();
        var src  = MockSource(AsyncRecords(new[] { ValidRecord() }));
        var sink = MockSink();

        var result = await pipeline.RunAsync(src, sink, new ExtractionContext(), cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cancel", "cancellation should produce a cancellation message");
    }

    [Fact]
    public async Task RunAsync_PopulatesResultMetadata()
    {
        var pipeline = NewPipeline();
        var src  = MockSource(AsyncRecords(new[] { ValidRecord() }));
        var sink = MockSink();
        var ctx  = new ExtractionContext { CorrelationId = "corr-123" };

        var result = await pipeline.RunAsync(src, sink, ctx);

        result.SourceName.Should().Be("test-source");
        result.DatasetKey.Should().Be("test_v1");
        result.CorrelationId.Should().Be("corr-123");
        result.StartedAt.Should().BeBefore(result.CompletedAt);
        result.Duration.Should().BePositive();
    }
}