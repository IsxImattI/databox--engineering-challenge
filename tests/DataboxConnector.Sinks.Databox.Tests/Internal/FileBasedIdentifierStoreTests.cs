using DataboxConnector.Sinks.Databox.Configuration;
using DataboxConnector.Sinks.Databox.Internal;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataboxConnector.Sinks.Databox.Tests.Internal;

public sealed class FileBasedIdentifierStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public FileBasedIdentifierStoreTests()
    {
        _tempDir  = Path.Combine(Path.GetTempPath(), "databox-tests-" + Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_tempDir, "identifiers.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileBasedIdentifierStore NewStore()
    {
        var options = Options.Create(new DataboxOptions
        {
            ApiKey = "test-key",
            IdentifierStorePath = _filePath
        });
        return new FileBasedIdentifierStore(options, NullLogger<FileBasedIdentifierStore>.Instance);
    }

    [Fact]
    public async Task GetDataSourceIdAsync_NoFile_ReturnsNull()
    {
        var store = NewStore();
        var result = await store.GetDataSourceIdAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAndGetDataSourceId_RoundTrips()
    {
        var store = NewStore();

        await store.SetDataSourceIdAsync("ds-123");
        var result = await store.GetDataSourceIdAsync();

        result.Should().Be("ds-123");
        File.Exists(_filePath).Should().BeTrue();
    }

    [Fact]
    public async Task SetDataSourceId_PersistsAcrossInstances()
    {
        var first = NewStore();
        await first.SetDataSourceIdAsync("ds-persisted");

        var second = NewStore();
        var result = await second.GetDataSourceIdAsync();

        result.Should().Be("ds-persisted");
    }

    [Fact]
    public async Task GetDatasetId_UnknownKey_ReturnsNull()
    {
        var store = NewStore();
        var result = await store.GetDatasetIdAsync("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAndGetDatasetId_KeyedSeparately()
    {
        var store = NewStore();

        await store.SetDatasetIdAsync("github_commits_v1", "dataset-abc");
        await store.SetDatasetIdAsync("spotify_tracks_v1", "dataset-xyz");

        (await store.GetDatasetIdAsync("github_commits_v1")).Should().Be("dataset-abc");
        (await store.GetDatasetIdAsync("spotify_tracks_v1")).Should().Be("dataset-xyz");
    }

    [Fact]
    public async Task SetDatasetId_OverwritesExisting()
    {
        var store = NewStore();
        await store.SetDatasetIdAsync("k", "v1");
        await store.SetDatasetIdAsync("k", "v2");

        (await store.GetDatasetIdAsync("k")).Should().Be("v2");
    }

    [Fact]
    public async Task Constructor_Resilient_CorruptFile_DoesNotThrow()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_filePath, "{ this is not valid JSON");

        var store = NewStore();

        var act = async () => await store.GetDataSourceIdAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConcurrentWrites_AllSucceed()
    {
        var store = NewStore();

        var tasks = Enumerable.Range(0, 20)
            .Select(i => store.SetDatasetIdAsync($"key_{i}", $"id_{i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        for (int i = 0; i < 20; i++)
            (await store.GetDatasetIdAsync($"key_{i}")).Should().Be($"id_{i}");
    }
}