using System.Net.Http.Headers;
using DataboxConnector.Sources.Spotify.OAuth;

namespace DataboxConnector.Sources.Spotify.Internal;

/// <summary>
/// HTTP handler that fetches a fresh access token from
/// <see cref="ISpotifyTokenProvider"/> and attaches it as a Bearer header
/// on every outbound request.
/// </summary>
/// <remarks>
/// Registered on the API client's <c>HttpClient</c> via
/// <c>AddHttpMessageHandler</c> in the DI extension. Centralizing auth here
/// keeps the API client free of token concerns.
/// </remarks>
internal sealed class SpotifyAuthHandler : DelegatingHandler
{
    private readonly ISpotifyTokenProvider _tokenProvider;

    public SpotifyAuthHandler(ISpotifyTokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var accessToken = await _tokenProvider
            .GetAccessTokenAsync(cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}