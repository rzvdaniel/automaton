namespace Automaton.Studio.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    // Optional runner to execute flows initiated by this app
    public Guid? ExecutionRunnerId { get; set; }

    public AppDefinition Definition { get; set; } = new AppDefinition();
}

public class AppDefinition
{
    public string? HtmlContent { get; set; }
    public IList<string>? ConnectedFlowIds { get; set; } = new List<string>();
    public AppSettings Settings { get; set; } = new AppSettings();
    public int Version { get; set; } = 1;
}

public class AppSettings
{
    public bool EnablePwa { get; set; } = false;
    public string? PwaName { get; set; }
    public string? PwaShortName { get; set; }
    public string? PwaIconUrl { get; set; }
    public string? ThemeColor { get; set; }
    public string? BackgroundColor { get; set; }
}

public class AppListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    // Optional ExecutionRunnerId for quick access in UI
    public Guid? ExecutionRunnerId { get; set; }
}

public class NewApp
{
    public string Name { get; set; }
    public Guid? ExecutionRunnerId { get; set; }
}
