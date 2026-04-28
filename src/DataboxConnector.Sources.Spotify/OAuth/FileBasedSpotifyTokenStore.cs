using System.Text.Json;
using DataboxConnector.Sources.Spotify.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sources.Spotify.OAuth;

/// <summary>
/// JSON-file-backed implementation of <see cref="ISpotifyTokenStore"/>.
/// </summary>
/// <remarks>
/// Access is serialized through a <see cref="SemaphoreSlim"/> to prevent
/// concurrent reads/writes from corrupting the file when refresh races
/// with another caller.
/// </remarks>
internal sealed class FileBasedSpotifyTokenStore : ISpotifyTokenStore
{
    private readonly string _filePath;
    private readonly ILogger<FileBasedSpotifyTokenStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileBasedSpotifyTokenStore(
        IOptions<SpotifyOptions> options,
        ILogger<FileBasedSpotifyTokenStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _filePath = Path.GetFullPath(options.Value.TokenStorePath);
        _logger = logger;
    }

    public async Task<SpotifyTokens?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("Spotify token store not found at {Path}.", _filePath);
                return null;
            }

            await using var stream = File.OpenRead(_filePath);
            var tokens = await JsonSerializer
                .DeserializeAsync<SpotifyTokens>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return tokens;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Spotify token store at {Path} is corrupt; re-run the auth bootstrap tool.", _filePath);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SpotifyTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var stream = File.Create(_filePath);
            await JsonSerializer
                .SerializeAsync(stream, tokens, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Spotify tokens saved to {Path}.", _filePath);
        }
        finally
        {
            _gate.Release();
        }
    }
}