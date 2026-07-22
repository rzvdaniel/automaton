using System.Text.RegularExpressions;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed partial class SecretStore(ApplicationDbContext db, ISecretProtector protector) : ISecretStore
{
    private static readonly IReadOnlySet<string> AllowedOwnerTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        SecretOwnerTypes.User,
        SecretOwnerTypes.Project,
        SecretOwnerTypes.App,
        SecretOwnerTypes.Platform,
        SecretOwnerTypes.RunnerHost,
        SecretOwnerTypes.HostedRunner
    };

    public async Task<SecretVersionCreatedResult> CreateVersionAsync(
        SecretOwner owner,
        string name,
        string plaintext,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (!AllowedOwnerTypes.Contains(owner.OwnerType) || owner.OwnerId == Guid.Empty)
            throw new ArgumentException("The secret owner is invalid.", nameof(owner));
        if (!SecretNameRegex().IsMatch(name))
            throw new ArgumentException("The secret name is invalid.", nameof(name));
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("A secret value is required.", nameof(plaintext));
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow)
            throw new ArgumentException("The secret expiry must be in the future.", nameof(expiresAtUtc));

        var now = DateTime.UtcNow;
        var definition = await db.SecretDefinitions
            .FirstOrDefaultAsync(x => x.OwnerType == owner.OwnerType
                && x.OwnerId == owner.OwnerId
                && x.Name == name,
                cancellationToken);
        if (definition == null)
        {
            definition = new SecretDefinition
            {
                Id = Guid.NewGuid(),
                OwnerType = owner.OwnerType,
                OwnerId = owner.OwnerId,
                Name = name,
                CreatedAtUtc = now
            };
            db.SecretDefinitions.Add(definition);
        }

        definition.DeletedAtUtc = null;
        definition.UpdatedAtUtc = now;
        definition.CurrentVersion++;
        var version = new SecretVersion
        {
            Id = Guid.NewGuid(),
            SecretDefinition = definition,
            Version = definition.CurrentVersion,
            ProtectedValue = protector.Protect(plaintext),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };
        db.SecretVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
        return new(definition.Id, version.Id, version.Version);
    }

    public async Task RevokeVersionAsync(Guid secretVersionId, CancellationToken cancellationToken)
    {
        var version = await db.SecretVersions.FirstOrDefaultAsync(x => x.Id == secretVersionId, cancellationToken);
        if (version == null)
            return;
        version.RevokedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretNameRegex();
}
