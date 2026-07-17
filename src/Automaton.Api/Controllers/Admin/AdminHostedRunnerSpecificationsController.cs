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
        var defaultId = await db.HostedRunnerPlatformSettings
            .AsNoTracking()
            .Select(x => (Guid?)x.DefaultSpecificationId)
            .SingleOrDefaultAsync(cancellationToken);
        var specifications = await db.HostedRunnerSpecifications
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return Ok(await Task.WhenAll(specifications.Select(x => ToDtoAsync(x, x.Id == defaultId, cancellationToken))));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HostedRunnerSpecificationDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FindAsync([id], cancellationToken);
        if (specification == null) return NotFound();
        var defaultId = await db.HostedRunnerPlatformSettings.AsNoTracking().Select(x => (Guid?)x.DefaultSpecificationId).SingleOrDefaultAsync(cancellationToken);
        return Ok(await ToDtoAsync(specification, specification.Id == defaultId, cancellationToken));
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
        return CreatedAtAction(nameof(Get), new { id = specification.Id }, await ToDtoAsync(specification, false, cancellationToken));
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
        var defaultId = await db.HostedRunnerPlatformSettings.AsNoTracking().Select(x => (Guid?)x.DefaultSpecificationId).SingleOrDefaultAsync(cancellationToken);
        return Ok(await ToDtoAsync(specification, specification.Id == defaultId, cancellationToken));
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<HostedRunnerSpecificationDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var source = await db.HostedRunnerSpecifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (source == null) return NotFound();
        var copy = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = $"{source.Code}-copy",
            Name = $"{source.Name} Copy",
            Description = source.Description,
            Resources = HostedRunnerSpecificationSnapshotFactory.CloneResources(source.Resources),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.HostedRunnerSpecifications.Add(copy);
        db.HostedRunnerSpecificationRevisions.Add(CreateRevision(copy, userContextService.GetUserId()));
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToDtoAsync(copy, false, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (specification == null) return NotFound();
        if (await db.HostedRunnerPlatformSettings.AnyAsync(x => x.DefaultSpecificationId == id, cancellationToken))
            return Conflict(new { message = "The platform default specification cannot be deleted." });
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
        var settings = await db.HostedRunnerPlatformSettings.AsNoTracking().Include(x => x.DefaultSpecification).SingleAsync(cancellationToken);
        return Ok(ToSettingsDto(settings));
    }

    [HttpPut("settings")]
    [HttpPut("~/api/admin/hosted-runner-settings")]
    public async Task<ActionResult<HostedRunnerPlatformSettingsDto>> UpdateSettings(UpdateHostedRunnerPlatformSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await db.HostedRunnerPlatformSettings.SingleAsync(cancellationToken);
        var specification = await db.HostedRunnerSpecifications.FindAsync([request.DefaultSpecificationId], cancellationToken);
        if (specification == null || !specification.IsActive) return BadRequest(new { message = "Default specification must be active and exist." });
        settings.DefaultSpecificationId = specification.Id;
        settings.ProvisioningEnabled = request.ProvisioningEnabled;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = userContextService.GetUserId();
        await db.SaveChangesAsync(cancellationToken);
        settings.DefaultSpecification = specification;
        return Ok(ToSettingsDto(settings));
    }

    private async Task<ActionResult<HostedRunnerSpecificationDto>> SetActive(Guid id, bool active, CancellationToken cancellationToken)
    {
        var specification = await db.HostedRunnerSpecifications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (specification == null) return NotFound();
        var isDefault = await db.HostedRunnerPlatformSettings.AnyAsync(x => x.DefaultSpecificationId == id, cancellationToken);
        if (!active && isDefault) return Conflict(new { message = "The platform default specification cannot be deactivated." });
        specification.IsActive = active;
        specification.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToDtoAsync(specification, isDefault, cancellationToken));
    }

    private async Task<HostedRunnerSpecificationDto> ToDtoAsync(HostedRunnerSpecification specification, bool isDefault, CancellationToken cancellationToken)
    {
        var runners = db.HostedRunners.Where(x => x.HostedRunnerSpecificationId == specification.Id && x.DeletedAt == null);
        return new HostedRunnerSpecificationDto(
            specification.Id, specification.Code, specification.Name, specification.Description,
            specification.Revision, specification.SchemaVersion, specification.IsActive, isDefault,
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
           && a.DiskLimitMb == b.DiskLimitMb && a.PidsLimit == b.PidsLimit && a.MaximumApps == b.MaximumApps;

    private static string? Validate(string? code, string name, HostedRunnerResources resources)
    {
        if (code != null && (string.IsNullOrWhiteSpace(code) || code.Length > 80)) return "Code is required and must be at most 80 characters.";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160) return "Name is required and must be at most 160 characters.";
        if (resources.CpuLimit is < HostedRunnerSpecificationLimits.MinimumCpuLimit or > HostedRunnerSpecificationLimits.MaximumCpuLimit) return "CPU limit is outside the supported range.";
        if (resources.MemoryLimitMb is < HostedRunnerSpecificationLimits.MinimumMemoryLimitMb or > HostedRunnerSpecificationLimits.MaximumMemoryLimitMb) return "Memory limit is outside the supported range.";
        if (resources.MemoryReservationMb < HostedRunnerSpecificationLimits.MinimumMemoryReservationMb || resources.MemoryReservationMb > resources.MemoryLimitMb) return "Memory reservation must be no greater than the memory limit.";
        if (resources.DiskLimitMb is < HostedRunnerSpecificationLimits.MinimumDiskLimitMb or > HostedRunnerSpecificationLimits.MaximumDiskLimitMb) return "Disk limit is outside the supported range.";
        if (resources.PidsLimit is < HostedRunnerSpecificationLimits.MinimumPidsLimit or > HostedRunnerSpecificationLimits.MaximumPidsLimit) return "PID limit is outside the supported range.";
        if (resources.MaximumApps is < HostedRunnerSpecificationLimits.MinimumApps or > HostedRunnerSpecificationLimits.MaximumApps) return "Maximum applications is outside the supported range.";
        return null;
    }

    private static HostedRunnerPlatformSettingsDto ToSettingsDto(HostedRunnerPlatformSettings settings)
        => new(settings.DefaultSpecificationId, settings.DefaultSpecification.Code, settings.ProvisioningEnabled, settings.UpdatedAtUtc);
}

public sealed record HostedRunnerResourcesDto(decimal CpuLimit, int MemoryLimitMb, int MemoryReservationMb, int DiskLimitMb, int PidsLimit, int MaximumApps);
public sealed record CreateHostedRunnerSpecificationRequest(string Name, string Code, string? Description, HostedRunnerResources Resources);
public sealed record UpdateHostedRunnerSpecificationRequest(string Name, string? Description, bool IsActive, HostedRunnerResources Resources);
public sealed record UpdateHostedRunnerPlatformSettingsRequest(Guid DefaultSpecificationId, bool ProvisioningEnabled);
public sealed record HostedRunnerSpecificationDto(Guid Id, string Code, string Name, string? Description, int Revision, int SchemaVersion, bool IsActive, bool IsDefault, HostedRunnerResources Resources, int RunnerCount, int OutdatedRunnerCount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record HostedRunnerPlatformSettingsDto(Guid DefaultSpecificationId, string DefaultSpecificationCode, bool ProvisioningEnabled, DateTime UpdatedAtUtc);
