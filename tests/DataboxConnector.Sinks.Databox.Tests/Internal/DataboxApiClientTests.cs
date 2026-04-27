using System.Net;
using System.Net.Http;
using System.Text.Json;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using DataboxConnector.Sinks.Databox.Internal;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Xunit;

namespace DataboxConnector.Sinks.Databox.Tests.Internal;

public class DataboxApiClientTests
{
    private static readonly Uri BaseAddress = new("https://api.databox.test");

    private static (DataboxApiClient client, MockHttpMessageHandler mock) NewClient()
    {
        var mock = new MockHttpMessageHandler();
        var http = new HttpClient(mock) { BaseAddress = BaseAddress };
        return (new DataboxApiClient(http, NullLogger<DataboxApiClient>.Instance), mock);
    }

    private static DatasetSchema MinimalSchema() => new(
        "test_v1",
        "Test Dataset",
        new[]
        {
            new FieldDefinition { Name = "id", Type = FieldType.String, IsPrimaryKey = true },
            new FieldDefinition { Name = "name", Type = FieldType.String }
        });

    // ---------- CreateDataSource ----------

    [Fact]
    public async Task CreateDataSourceAsync_Success_ReturnsId()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Post, BaseAddress + "v1/data-sources")
            .Respond("application/json", """
                {
                    "requestId": "req-1",
                    "status": "success",
                    "id": "ds-123",
                    "title": "My Source"
                }
                """);

        var id = await client.CreateDataSourceAsync("My Source");

        id.Should().Be("ds-123");
    }

    [Fact]
    public async Task CreateDataSourceAsync_4xxErrorEnvelope_ThrowsWithDetails()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Post, BaseAddress + "v1/data-sources")
            .Respond(HttpStatusCode.BadRequest, "application/json", """
                {
                    "requestId": "req-2",
                    "status": "error",
                    "errors": [
                        { "code": "invalid_input", "field": "title", "type": "validation",
                          "message": "Title is invalid." }
                    ]
                }
                """);

        var act = async () => await client.CreateDataSourceAsync("oops");

        var ex = await act.Should().ThrowAsync<SinkIngestionException>();
        ex.Which.Message.Should().Contain("invalid_input");
        ex.Which.Message.Should().Contain("Title is invalid.");
    }

    [Fact]
    public async Task CreateDataSourceAsync_5xx_Throws()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Post, BaseAddress + "v1/data-sources")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        var act = async () => await client.CreateDataSourceAsync("X");

        await act.Should().ThrowAsync<SinkIngestionException>();
    }

    [Fact]
    public async Task CreateDataSourceAsync_EmptyId_Throws()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Post, BaseAddress + "v1/data-sources")
            .Respond("application/json", """
                { "requestId": "r", "status": "success" }
                """);

        var act = async () => await client.CreateDataSourceAsync("X");

        await act.Should().ThrowAsync<SinkIngestionException>()
            .WithMessage("*no data source id*");
    }

    // ---------- CreateDataset ----------

    [Fact]
    public async Task CreateDatasetAsync_Success_ReturnsId()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Post, BaseAddress + "v1/datasets")
            .Respond("application/json", """
                {
                    "requestId": "r",
                    "status": "success",
                    "id": "dataset-uuid",
                    "title": "Test Dataset"
                }
                """);

        var id = await client.CreateDatasetAsync("12345", MinimalSchema());

        id.Should().Be("dataset-uuid");
    }

    [Fact]
    public async Task CreateDatasetAsync_SendsPrimaryKeysFromSchema()
    {
        var (client, mock) = NewClient();

        string? capturedBody = null;
        mock.When(HttpMethod.Post, BaseAddress + "v1/datasets")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", """
                { "requestId": "r", "status": "success", "id": "did" }
                """);

        await client.CreateDatasetAsync("12345", MinimalSchema());

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("primaryKeys").EnumerateArray()
            .Select(e => e.GetString()).Should().ContainSingle().Which.Should().Be("id");
    }

    // ---------- IngestRecords ----------

    [Fact]
    public async Task IngestRecordsAsync_Success_ReturnsIngestionId()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Post, BaseAddress + "v1/datasets/did-1/data")
            .Respond("application/json", """
                {
                    "requestId": "r",
                    "status": "success",
                    "ingestionId": "ing-001",
                    "message": "Data ingestion request accepted"
                }
                """);

        var record = RawRecord.From(new Dictionary<string, object?>
        {
            ["id"] = "x",
            ["name"] = "y"
        });

        var id = await client.IngestRecordsAsync("did-1", new[] { record });

        id.Should().Be("ing-001");
    }

    [Fact]
    public async Task IngestRecordsAsync_EmptyBatch_ThrowsArgumentException()
    {
        var (client, _) = NewClient();

        var act = async () => await client.IngestRecordsAsync("did-1", Array.Empty<RawRecord>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IngestRecordsAsync_BatchTooLarge_ThrowsArgumentException()
    {
        var (client, _) = NewClient();

        var batch = Enumerable.Range(0, 101)
            .Select(i => RawRecord.From(new Dictionary<string, object?> { ["id"] = $"{i}" }))
            .ToList();

        var act = async () => await client.IngestRecordsAsync("did-1", batch);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*100*");
    }

    [Fact]
    public async Task IngestRecordsAsync_SendsRecordsAsArray()
    {
        var (client, mock) = NewClient();

        string? capturedBody = null;
        mock.When(HttpMethod.Post, BaseAddress + "v1/datasets/did-1/data")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", """
                { "requestId": "r", "status": "success", "ingestionId": "i" }
                """);

        var batch = new[]
        {
            RawRecord.From(new Dictionary<string, object?> { ["id"] = "1", ["name"] = "a" }),
            RawRecord.From(new Dictionary<string, object?> { ["id"] = "2", ["name"] = "b" })
        };

        await client.IngestRecordsAsync("did-1", batch);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("records").GetArrayLength().Should().Be(2);
    }
}