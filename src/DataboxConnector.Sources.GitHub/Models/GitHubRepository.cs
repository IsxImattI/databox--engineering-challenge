namespace DataboxConnector.Sources.GitHub.Models;

/// <summary>
/// Repository identifier parsed from the configured <c>owner/name</c> string.
/// </summary>
internal readonly record struct GitHubRepository(string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";

    public static GitHubRepository Parse(string ownerSlashName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSlashName);

        var parts = ownerSlashName.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
            throw new ArgumentException(
                $"Repository must be in 'owner/name' format. Got: '{ownerSlashName}'.",
                nameof(ownerSlashName));

        return new GitHubRepository(parts[0], parts[1]);
    }
}