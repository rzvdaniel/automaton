using Automaton.Core.Models;

namespace Automaton.Api.Models;

/// <summary>One installable app in the catalog, as the Catalog tab shows it.</summary>
public sealed record CatalogEntryDto(
    string Id,
    string Name,
    string Summary,
    string Category,
    string Repo,
    string Folder,
    string Commit,
    string MinServerVersion,
    IReadOnlyList<string> Permissions,
    bool RequiresNewerServer,
    Guid? InstalledAppId,
    string SourceId,
    string SourceName,
    bool CustomSource);

/// <summary>The catalog plus where it came from: fresh, the last good copy (Stale), or nothing at all (Unavailable).</summary>
public sealed record CatalogResponse(
    IReadOnlyList<CatalogEntryDto> Apps,
    DateTime? FetchedAt,
    bool Stale,
    bool Unavailable,
    string? Message);

/// <summary>A source pinned to one commit: repository (owner/name), folder inside it (empty for the root) and the commit.</summary>
public sealed record GitAppSource(string Repo, string Folder, string Commit);

/// <summary>What an install page needs to show before anything is created.</summary>
public sealed class CatalogInstallSourceModel
{
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string Repo { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Commit { get; set; } = string.Empty;
    public ApplicationContract? Application { get; set; }
}

public sealed class CatalogInstallRequest
{
    public string Repo { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public string Commit { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid? RunnerId { get; set; }
}

/// <summary>A catalog source as an administrator sees it. The token is never returned, only whether one is saved.</summary>
public sealed record CatalogSourceDto(string Id, string Name, string Repo, string Branch, bool Official, bool Enabled, bool HasToken);

public sealed class CatalogSourceRequest
{
    public string Repo { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public string? Name { get; set; }
    public string? Token { get; set; }
}

public sealed class CatalogSourceEnabledRequest
{
    public bool Enabled { get; set; }
}
