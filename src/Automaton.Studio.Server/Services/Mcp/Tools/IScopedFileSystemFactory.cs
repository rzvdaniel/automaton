using System.Text.Json;

namespace Automaton.Studio.Server.Services.Mcp.Tools;

/// <summary>
/// Resolves the correct <see cref="IScopedFileSystem"/> for a tool invocation.
/// The factory reads the resource identifier (e.g. appId, flowId) from the tool
/// arguments or conversation context, so <see cref="FileIoTool"/> stays resource-agnostic.
/// </summary>
public interface IScopedFileSystemFactory
{
    IScopedFileSystem Create(JsonElement arguments, Dictionary<string, object>? context);
}
