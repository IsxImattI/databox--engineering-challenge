using DataboxConnector.Sources.Spotify.Configuration;
using DataboxConnector.Sources.Spotify.OAuth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataboxConnector.Sources.Spotify.Tests.OAuth;

public sealed class FileBasedSpotifyTokenStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public FileBasedSpotifyTokenStoreTests()
    {
        _tempDir  = Path.Combine(Path.GetTempPath(), "spotify-token-tests-" + Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_tempDir, "tokens.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileBasedSpotifyTokenStore NewStore()
    {
        var options = Options.Create(new SpotifyOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            TokenStorePath = _filePath
        });
        return new FileBasedSpotifyTokenStore(options, NullLogger<FileBasedSpotifyTokenStore>.Instance);
    }

    private static SpotifyTokens NewTokens(string suffix = "1") => new()
    {
        AccessToken = $"access-{suffix}",
        RefreshToken = $"refresh-{suffix}",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsNull()
    {
        var result = await NewStore().LoadAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = NewStore();
        var original = NewTokens();

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync();

        loaded.Should().NotBeNull();
        loaded!.AccessToken.Should().Be(original.AccessToken);
        loaded.RefreshToken.Should().Be(original.RefreshToken);
    }

    [Fact]
    public async Task SaveAsync_PersistsAcrossInstances()
    {
        var first = NewStore();
        await first.SaveAsync(NewTokens("persisted"));

        var second = NewStore();
        var loaded = await second.LoadAsync();

        loaded!.AccessToken.Should().Be("access-persisted");
    }

    [Fact]
    public async Task SaveAsync_OverwritesExisting()
    {
        var store = NewStore();
        await store.SaveAsync(NewTokens("v1"));
        await store.SaveAsync(NewTokens("v2"));

        var loaded = await store.LoadAsync();
        loaded!.AccessToken.Should().Be("access-v2");
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsNull()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_filePath, "{ this is not valid JSON");

        var result = await NewStore().LoadAsync();

        result.Should().BeNull();
    }
}