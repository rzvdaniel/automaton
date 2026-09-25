using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hosted-runner-specifications")]
[Authorize(Policy = "Administrators")]
public sealed class AdminHostedRunnerSpecificationsController(
    ApplicationDbContext db,
    UserContextService userContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HostedRunnerSpecificationDto>>> List(CancellationToken cancellationToken)
    {
        var specifications = await db.HostedRunnerSpecifications
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return Ok(await Task.WhenAll(specifications.Select(x => ToDtoAsync(x, cancellationToken))));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HostedRunnerSpecificationDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FindAsync([id], cancellationToken);
        if (specification == null) return NotFound();
        return Ok(await ToDtoAsync(specification, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<HostedRunnerSpecificationDto>> Create(CreateHostedRunnerSpecificationRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request.Code, request.Name, request.Resources);
        if (validation != null) return BadRequest(new { message = validation });
        if (await db.HostedRunnerSpecifications.AnyAsync(x => x.Code == request.Code.Trim(), cancellationToken))
            return Conflict(new { message = "Specification code already exists." });

        var now = DateTime.UtcNow;
        var specification = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Resources = request.Resources,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.HostedRunnerSpecifications.Add(specification);
        db.HostedRunnerSpecificationRevisions.Add(CreateRevision(specification, userContextService.GetUserId()));
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = specification.Id }, await ToDtoAsync(specification, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HostedRunnerSpecificationDto>> Update(Guid id, UpdateHostedRunnerSpecificationRequest request, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (specification == null) return NotFound();
        var validation = Validate(null, request.Name, request.Resources);
        if (validation != null) return BadRequest(new { message = validation });

        var changed = !ResourcesEqual(specification.Resources, request.Resources)
            || !string.Equals(specification.Name, request.Name.Trim(), StringComparison.Ordinal)
            || !string.Equals(specification.Description, request.Description?.Trim(), StringComparison.Ordinal);
        specification.Name = request.Name.Trim();
        specification.Description = request.Description?.Trim();
        specification.IsActive = request.IsActive;
        specification.UpdatedAtUtc = DateTime.UtcNow;
        if (changed)
        {
            specification.Revision++;
            specification.Resources = request.Resources;
            db.HostedRunnerSpecificationRevisions.Add(CreateRevision(specification, userContextService.GetUserId()));
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToDtoAsync(specification, cancellationToken));
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<HostedRunnerSpecificationDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var source = await db.HostedRunnerSpecifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (source == null) return NotFound();
        var code = await BuildDuplicateCodeAsync(source.Code, cancellationToken);
        var copy = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"{source.Name} Copy",
            Description = source.Description,
            Resources = HostedRunnerSpecificationSnapshotFactory.CloneResources(source.Resources),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.HostedRunnerSpecifications.Add(copy);
        db.HostedRunnerSpecificationRevisions.Add(CreateRevision(copy, userContextService.GetUserId()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "A specification with the generated duplicate code already exists. Please try again." });
        }
        return Ok(await ToDtoAsync(copy, cancellationToken));
    }

    private async Task<string> BuildDuplicateCodeAsync(string sourceCode, CancellationToken cancellationToken)
    {
        const string firstSuffix = "-copy";
        var root = sourceCode.Trim();
        var candidate = $"{root[..Math.Min(root.Length, 80 - firstSuffix.Length)]}{firstSuffix}";
        if (!await db.HostedRunnerSpecifications.AnyAsync(x => x.Code == candidate, cancellationToken))
        {
            return candidate;
        }

        for (var suffixNumber = 2; ; suffixNumber++)
        {
            var suffix = $"-copy-{suffixNumber}";
            candidate = $"{root[..Math.Min(root.Length, 80 - suffix.Length)]}{suffix}";
            if (!await db.HostedRunnerSpecifications.AnyAsync(x => x.Code == candidate, cancellationToken))
            {
                return candidate;
            }
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (specification == null) return NotFound();
        if (await db.HostedRunners.AnyAsync(x => x.HostedRunnerSpecificationId == id && x.DeletedAt == null, cancellationToken))
            return Conflict(new { message = "A specification used by hosted runners cannot be deleted." });

        db.HostedRunnerSpecifications.Remove(specification);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public Task<ActionResult<HostedRunnerSpecificationDto>> Activate(Guid id, CancellationToken cancellationToken) => SetActive(id, true, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    public Task<ActionResult<HostedRunnerSpecificationDto>> Deactivate(Guid id, CancellationToken cancellationToken) => SetActive(id, false, cancellationToken);

    [HttpGet("settings")]
    [HttpGet("~/api/admin/hosted-runner-settings")]
    public async Task<ActionResult<HostedRunnerPlatformSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await db.HostedRunnerPlatformSettings.AsNoTracking().SingleAsync(cancellationToken);
        return Ok(ToSettingsDto(settings));
    }

    [HttpPut("settings")]
    [HttpPut("~/api/admin/hosted-runner-settings")]
    public async Task<ActionResult<HostedRunnerPlatformSettingsDto>> UpdateSettings(UpdateHostedRunnerPlatformSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await db.HostedRunnerPlatformSettings.SingleAsync(cancellationToken);
        settings.ProvisioningEnabled = request.ProvisioningEnabled;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = userContextService.GetUserId();
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToSettingsDto(settings));
    }

    private async Task<ActionResult<HostedRunnerSpecificationDto>> SetActive(Guid id, bool active, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (specification == null) return NotFound();
        specification.IsActive = active;
        specification.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToDtoAsync(specification, cancellationToken));
    }

    private async Task<HostedRunnerSpecificationDto> ToDtoAsync(HostedRunnerSpecification specification, CancellationToken cancellationToken)
    {
        var runners = db.HostedRunners.Where(x => x.HostedRunnerSpecificationId == specification.Id && x.DeletedAt == null);
        return new HostedRunnerSpecificationDto(
            specification.Id, specification.Code, specification.Name, specification.Description,
            specification.Revision, specification.SchemaVersion, specification.IsActive,
            specification.Resources,
            await runners.CountAsync(cancellationToken),
            await runners.CountAsync(x => x.SpecificationRevision < specification.Revision, cancellationToken),
            specification.CreatedAtUtc, specification.UpdatedAtUtc);
    }

    private static HostedRunnerSpecificationRevision CreateRevision(HostedRunnerSpecification specification, Guid userId) => new()
    {
        Id = Guid.NewGuid(), SpecificationId = specification.Id, Revision = specification.Revision,
        SchemaVersion = specification.SchemaVersion, Resources = HostedRunnerSpecificationSnapshotFactory.CloneResources(specification.Resources),
        Name = specification.Name, Description = specification.Description, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = userId
    };

    private static bool ResourcesEqual(HostedRunnerResources a, HostedRunnerResources b)
        => a.CpuLimit == b.CpuLimit && a.MemoryLimitMb == b.MemoryLimitMb && a.MemoryReservationMb == b.MemoryReservationMb
           && a.DiskLimitMb == b.DiskLimitMb && a.PidsLimit == b.PidsLimit
           && a.SharedMemoryMb == b.SharedMemoryMb
           && a.DockerLogMaxSizeMb == b.DockerLogMaxSizeMb
           && a.DockerLogMaxFiles == b.DockerLogMaxFiles
           && a.MaximumApps == b.MaximumApps;

    internal static string? Validate(string? code, string name, HostedRunnerResources resources)
    {
        if (code != null && (string.IsNullOrWhiteSpace(code) || code.Length > 80)) return "Code is required and must be at most 80 characters.";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160) return "Name is required and must be at most 160 characters.";
        return ValidateResourceLimits(resources);
    }

    private static string? ValidateResourceLimits(HostedRunnerResources resources)
    {
        return ValidateComputeResources(resources)
            ?? ValidateExecutionResources(resources)
            ?? ValidateLoggingResources(resources)
            ?? ValidateApplicationLimit(resources);
    }

    private static string? ValidateComputeResources(HostedRunnerResources resources)
    {
        if (resources.CpuLimit is < HostedRunnerSpecificationLimits.MinimumCpuLimit or > HostedRunnerSpecificationLimits.MaximumCpuLimit) return "CPU limit is outside the supported range.";
        if (resources.MemoryLimitMb is < HostedRunnerSpecificationLimits.MinimumMemoryLimitMb or > HostedRunnerSpecificationLimits.MaximumMemoryLimitMb) return "Memory limit is outside the supported range.";
        if (resources.MemoryReservationMb < HostedRunnerSpecificationLimits.MinimumMemoryReservationMb || resources.MemoryReservationMb > resources.MemoryLimitMb) return "Memory reservation must be no greater than the memory limit.";
        if (resources.DiskLimitMb is < HostedRunnerSpecificationLimits.MinimumDiskLimitMb or > HostedRunnerSpecificationLimits.MaximumDiskLimitMb) return "Disk limit is outside the supported range.";
        return null;
    }

    private static string? ValidateExecutionResources(HostedRunnerResources resources)
    {
        if (resources.PidsLimit is < HostedRunnerSpecificationLimits.MinimumPidsLimit or > HostedRunnerSpecificationLimits.MaximumPidsLimit) return "PID limit is outside the supported range.";
        if (resources.SharedMemoryMb is < HostedRunnerSpecificationLimits.MinimumSharedMemoryMb or > HostedRunnerSpecificationLimits.MaximumSharedMemoryMb) return "Shared memory limit is outside the supported range.";
        return null;
    }

    private static string? ValidateLoggingResources(HostedRunnerResources resources)
    {
        if (resources.DockerLogMaxSizeMb is < HostedRunnerSpecificationLimits.MinimumDockerLogMaxSizeMb or > HostedRunnerSpecificationLimits.MaximumDockerLogMaxSizeMb) return "Docker log size limit is outside the supported range.";
        if (resources.DockerLogMaxFiles is < HostedRunnerSpecificationLimits.MinimumDockerLogMaxFiles or > HostedRunnerSpecificationLimits.MaximumDockerLogMaxFiles) return "Docker log file count is outside the supported range.";
        return null;
    }

    private static string? ValidateApplicationLimit(HostedRunnerResources resources)
    {
        if (resources.MaximumApps is < HostedRunnerSpecificationLimits.MinimumApps or > HostedRunnerSpecificationLimits.MaximumApps) return "Maximum applications is outside the supported range.";
        return null;
    }

    private static HostedRunnerPlatformSettingsDto ToSettingsDto(HostedRunnerPlatformSettings settings)
        => new(settings.ProvisioningEnabled, settings.UpdatedAtUtc);
}

public sealed record HostedRunnerResourcesDto(decimal CpuLimit, int MemoryLimitMb, int MemoryReservationMb, int DiskLimitMb, int PidsLimit, int SharedMemoryMb, int DockerLogMaxSizeMb, int DockerLogMaxFiles, int MaximumApps);
public sealed record CreateHostedRunnerSpecificationRequest(string Name, string Code, string? Description, HostedRunnerResources Resources);
public sealed record UpdateHostedRunnerSpecificationRequest(string Name, string? Description, bool IsActive, HostedRunnerResources Resources);
public sealed record UpdateHostedRunnerPlatformSettingsRequest(bool ProvisioningEnabled);
public sealed record HostedRunnerSpecificationDto(Guid Id, string Code, string Name, string? Description, int Revision, int SchemaVersion, bool IsActive, HostedRunnerResources Resources, int RunnerCount, int OutdatedRunnerCount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record HostedRunnerPlatformSettingsDto(bool ProvisioningEnabled, DateTime UpdatedAtUtc);
