using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataboxConnector.Sources.Spotify.OAuth;
using DataboxConnector.Tools.SpotifyAuth;
using Microsoft.Extensions.Configuration;

// === Configuration ===
// Read from appsettings.json + user secrets + env vars, in that order.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<SpotifyAuthMarker>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var clientId = configuration["Sources:Spotify:ClientId"]
    ?? throw new InvalidOperationException(
        "Missing Sources:Spotify:ClientId. Set it in appsettings.json, user secrets, or env.");

var clientSecret = configuration["Sources:Spotify:ClientSecret"]; // optional with PKCE

var redirectUri = configuration["Sources:Spotify:RedirectUri"]
    ?? "http://localhost:8888/callback";

var tokenStorePath = configuration["Sources:Spotify:TokenStorePath"]
    ?? "data/spotify-tokens.json";

var accountsBase = configuration["Sources:Spotify:AccountsBaseUrl"]
    ?? "https://accounts.spotify.com";

// Scopes we need: recently played + top items.
const string scopes = "user-read-recently-played user-top-read";

// === PKCE: generate code verifier + challenge ===
var codeVerifier = GenerateCodeVerifier();
var codeChallenge = GenerateCodeChallenge(codeVerifier);

// State protects against CSRF on the redirect.
var state = GenerateState();

// === Build the authorization URL ===
var authQuery = new Dictionary<string, string>
{
    ["client_id"]             = clientId,
    ["response_type"]         = "code",
    ["redirect_uri"]          = redirectUri,
    ["scope"]                 = scopes,
    ["state"]                 = state,
    ["code_challenge_method"] = "S256",
    ["code_challenge"]        = codeChallenge
};

var authUrl = $"{accountsBase.TrimEnd('/')}/authorize?" +
              string.Join("&", authQuery.Select(kv =>
                  $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

// === Start local HTTP listener for the callback ===
using var listener = new HttpListener();
var listenerPrefix = redirectUri.EndsWith('/') ? redirectUri : redirectUri + "/";
listener.Prefixes.Add(listenerPrefix);

try
{
    listener.Start();
}
catch (HttpListenerException ex)
{
    Console.Error.WriteLine(
        $"Could not bind listener to {listenerPrefix}.\n" +
        $"On Windows, you may need to run as administrator or reserve the URL with:\n" +
        $"  netsh http add urlacl url={listenerPrefix} user=Everyone\n\n" +
        $"Detail: {ex.Message}");
    return 1;
}

Console.WriteLine($"Listening for Spotify callback on {listenerPrefix}");
Console.WriteLine($"Opening browser to authorize...");
Console.WriteLine();

OpenBrowser(authUrl);

Console.WriteLine($"If the browser did not open automatically, visit:");
Console.WriteLine();
Console.WriteLine($"  {authUrl}");
Console.WriteLine();

// === Wait for the redirect ===
var context = await listener.GetContextAsync();
var query = context.Request.Url!.Query;
var queryParams = ParseQuery(query);

// Always close the browser tab gracefully.
await RespondWithHtmlAsync(context.Response,
    """
    <html><body style="font-family: sans-serif; padding: 2rem;">
      <h1>✓ Spotify authorization complete</h1>
      <p>You can close this tab and return to the terminal.</p>
    </body></html>
    """);

// === Validate state ===
if (!queryParams.TryGetValue("state", out var receivedState) || receivedState != state)
{
    Console.Error.WriteLine("State parameter mismatch. Possible CSRF; aborting.");
    return 1;
}

if (queryParams.TryGetValue("error", out var error))
{
    Console.Error.WriteLine($"Spotify returned an authorization error: {error}");
    return 1;
}

if (!queryParams.TryGetValue("code", out var code))
{
    Console.Error.WriteLine("Authorization code missing from callback.");
    return 1;
}

// === Exchange the code for tokens ===
Console.WriteLine("Exchanging authorization code for tokens...");

using var http = new HttpClient { BaseAddress = new Uri(accountsBase) };

var tokenForm = new Dictionary<string, string>
{
    ["grant_type"]    = "authorization_code",
    ["code"]          = code,
    ["redirect_uri"]  = redirectUri,
    ["client_id"]     = clientId,
    ["code_verifier"] = codeVerifier
};

// If a client secret is configured, also include it (Spotify accepts both
// PKCE-only and PKCE+secret on confidential clients).
if (!string.IsNullOrEmpty(clientSecret))
    tokenForm["client_secret"] = clientSecret;

using var response = await http.PostAsync("/api/token", new FormUrlEncodedContent(tokenForm));

if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync();
    Console.Error.WriteLine($"Token exchange failed: HTTP {(int)response.StatusCode}");
    Console.Error.WriteLine(body);
    return 1;
}

var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

var accessToken = payload.GetProperty("access_token").GetString()
    ?? throw new InvalidOperationException("No access_token in response.");
var refreshToken = payload.GetProperty("refresh_token").GetString()
    ?? throw new InvalidOperationException("No refresh_token in response.");
var expiresIn = payload.GetProperty("expires_in").GetInt32();

var tokens = new SpotifyTokens
{
    AccessToken = accessToken,
    RefreshToken = refreshToken,
    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
};

// === Save to disk ===
var fullPath = Path.GetFullPath(tokenStorePath);
var directory = Path.GetDirectoryName(fullPath);
if (!string.IsNullOrEmpty(directory))
    Directory.CreateDirectory(directory);

await using var fileStream = File.Create(fullPath);
await JsonSerializer.SerializeAsync(fileStream, tokens,
    new JsonSerializerOptions { WriteIndented = true });

Console.WriteLine();
Console.WriteLine($"✓ Tokens saved to: {fullPath}");
Console.WriteLine($"  Access token expires at: {tokens.AccessTokenExpiresAt:u}");
Console.WriteLine();
Console.WriteLine("You can now run the connector. The host will read these tokens");
Console.WriteLine("and refresh them automatically when they expire.");

return 0;

// =================================================================
// === Local helpers ===
// =================================================================

static string GenerateCodeVerifier()
{
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return Base64UrlEncode(bytes);
}

static string GenerateCodeChallenge(string codeVerifier)
{
    var bytes = Encoding.ASCII.GetBytes(codeVerifier);
    var hash = SHA256.HashData(bytes);
    return Base64UrlEncode(hash);
}

static string GenerateState()
{
    Span<byte> bytes = stackalloc byte[16];
    RandomNumberGenerator.Fill(bytes);
    return Base64UrlEncode(bytes);
}

static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
        // Browser failed to open - user will see the URL in console and can copy/paste.
    }
}

static async Task RespondWithHtmlAsync(HttpListenerResponse response, string html)
{
    var bytes = Encoding.UTF8.GetBytes(html);
    response.ContentType = "text/html; charset=utf-8";
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
    response.Close();
}

static Dictionary<string, string> ParseQuery(string query)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrEmpty(query)) return result;

    foreach (var pair in query.TrimStart('?').Split('&'))
    {
        var idx = pair.IndexOf('=');
        if (idx < 0) continue;
        var key = Uri.UnescapeDataString(pair[..idx]);
        var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
        result[key] = value;
    }
    return result;
}

// Marker type for User Secrets — they are scoped to the assembly that owns this type.
namespace DataboxConnector.Tools.SpotifyAuth
{
    internal sealed class SpotifyAuthMarker { }
}