using System.Text.RegularExpressions;

namespace DataboxConnector.Sources.GitHub.Internal;

/// <summary>
/// Parses RFC 5988 <c>Link</c> headers as used by the GitHub REST API for pagination.
/// </summary>
/// <remarks>
/// Example header value:
/// <code>
/// &lt;https://api.github.com/.../commits?page=2&gt;; rel="next",
/// &lt;https://api.github.com/.../commits?page=5&gt;; rel="last"
/// </code>
/// </remarks>
internal static partial class LinkHeaderParser
{
    [GeneratedRegex(@"<(?<url>[^>]+)>;\s*rel=""(?<rel>[^""]+)""", RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    /// <summary>
    /// Returns the URL whose <c>rel</c> equals <paramref name="rel"/>, or <c>null</c>
    /// if no such link is present.
    /// </summary>
    public static Uri? GetUrl(string? linkHeaderValue, string rel)
    {
        if (string.IsNullOrWhiteSpace(linkHeaderValue))
            return null;

        ArgumentException.ThrowIfNullOrWhiteSpace(rel);

        foreach (Match match in LinkRegex().Matches(linkHeaderValue))
        {
            if (match.Groups["rel"].Value.Equals(rel, StringComparison.OrdinalIgnoreCase))
                return new Uri(match.Groups["url"].Value);
        }

        return null;
    }

    /// <summary>
    /// Convenience overload accepting <see cref="HttpResponseHeaders"/>.
    /// </summary>
    public static Uri? GetNextUrl(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
            return null;

        return GetUrl(string.Join(", ", values), "next");
    }
}