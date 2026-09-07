using System.IO.Compression;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

internal static class DeployPackageContractService
{
    public static async Task<ApplicationContract> ReadAsync(
        IFormFile file,
        Guid expectedAppId,
        CancellationToken cancellationToken)
    {
        using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifest = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName.TrimStart('/'), "manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifest is null)
        {
            throw new InvalidOperationException("Deploy package manifest.json is required.");
        }

        await using var manifestStream = manifest.Open();
        using var document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var appId = GetRequiredProperty(root, "appId").GetString();
        if (!Guid.TryParse(appId, out var manifestAppId) || manifestAppId != expectedAppId)
        {
            throw new InvalidOperationException("Deploy package manifest appId does not match the requested app.");
        }

        var application = GetRequiredProperty(root, "application");
        var contract = ApplicationContractSerialization.Deserialize(application.GetRawText());
        ApplicationContractValidator.EnsureDeployable(contract);
        return contract;
    }

    public static async Task PersistAsync(
        ApplicationDbContext dbContext,
        Guid appId,
        ApplicationContract application,
        CancellationToken cancellationToken)
    {
        var app = await dbContext.WebApps.SingleOrDefaultAsync(x => x.Id == appId, cancellationToken);
        if (app is null)
        {
            throw new InvalidOperationException($"App {appId} not found.");
        }

        app.ApplicationContractJson = ApplicationContractSerialization.Serialize(application);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static JsonElement GetRequiredProperty(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Deploy package manifest must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException($"Deploy package manifest property '{name}' is required.");
    }
}
