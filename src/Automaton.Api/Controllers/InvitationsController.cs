using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Enums;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Data;

namespace Automaton.Api.Controllers;

[Route("api/invitations")]
public class InvitationsController : ControllerBase
{
    private readonly InvitationTokenService invitationService;
    private readonly ApplicationDbContext db;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly UserContextService userContextService;
    private readonly UsersService usersService;

    public InvitationsController(
        InvitationTokenService invitationService,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        UserContextService userContextService,
        UsersService usersService)
    {
        this.invitationService = invitationService;
        this.db = db;
        this.userManager = userManager;
        this.signInManager = signInManager;
        this.userContextService = userContextService;
        this.usersService = usersService;
    }

    [HttpGet("validate/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Validate(string token)
    {
        var invite = await invitationService.ValidateInvitationToken(token);
        if (invite == null) return NotFound();

        // Determine if invitee requires password setup: user missing or has no local password
        var user = await userManager.FindByEmailAsync(invite.Email);
        var requirePasswordSetup = user == null || !(await userManager.HasPasswordAsync(user));

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == invite.ProjectId);
        var projectName = project?.DisplayName ?? string.Empty;

        return Ok(new { projectId = invite.ProjectId, projectName, email = invite.Email, role = invite.Role, invitedBy = invite.InvitedBy, requirePasswordSetup });
    }

    // Accept for authenticated users
    [Authorize]
    [HttpPost("accept/{token}")]
    public async Task<IActionResult> Accept(string token)
    {
        var invite = await invitationService.ValidateInvitationToken(token);
        if (invite == null) return BadRequest(new { message = "Invalid or expired token" });

        // Ensure current user matches email on invitation
        var userId = userContextService.GetUserId();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Unauthorized();

        if (!string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Authenticated user does not match invitation email" });
        }

        // Add membership
        var existing = await db.ProjectMembers.FirstOrDefaultAsync(pm => pm.ProjectId == invite.ProjectId && pm.UserId == user.Id);
        if (existing != null)
        {
            if (existing.IsActive)
            {
                await invitationService.ConsumeInvitationToken(token);
                return Ok(new { message = "Already a member" });
            }

            existing.IsActive = true;
            existing.Role = Enum.TryParse<ProjectRole>(invite.Role, out var r) ? r : ProjectRole.Viewer;
            existing.InvitedBy = invite.InvitedBy;
            existing.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = invite.ProjectId,
                UserId = user.Id,
                Role = Enum.TryParse<ProjectRole>(invite.Role, out var r2) ? r2 : ProjectRole.Viewer,
                JoinedAt = DateTime.UtcNow,
                InvitedBy = invite.InvitedBy,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
        await invitationService.ConsumeInvitationToken(token);

        return Ok(new { message = "Invitation accepted" });
    }

    public class AcceptInvitationRequest
    {
        public string? Name { get; set; }
        public string Password { get; set; } = string.Empty;
    }

    // Combined accept + setup for unauthenticated users.
    // This is only for first-time invitees or accounts without a local password.
    // Existing password-bearing accounts must authenticate and use the normal accept flow.
    [AllowAnonymous]
    [HttpPost("accept/{token}/complete")]
    public async Task<IActionResult> Complete(string token, [FromBody] AcceptInvitationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Password))
            return BadRequest(new { message = "Password is required" });

        var invite = await invitationService.ValidateInvitationToken(token);
        if (invite == null) return BadRequest(new { message = "Invalid or expired token" });

        if (!await db.Projects.AnyAsync(project => project.Id == invite.ProjectId))
            return BadRequest(new { message = "Project not found" });

        var preparation = await PrepareInviteeAsync(invite, request);
        if (preparation.Error is not null)
            return preparation.Error;

        var user = preparation.User!;
        await EnsureProjectMembershipAsync(invite, user);

        await db.SaveChangesAsync();

        await invitationService.ConsumeInvitationToken(token);

        await usersService.MarkSuccessfulLoginAsync(user);
        await signInManager.SignInAsync(user, isPersistent: false);

        return Ok(new { message = "Invitation accepted and account setup complete", redirect = "/projects" });
    }

    private async Task<InviteePreparationResult> PrepareInviteeAsync(
        InvitationToken invite,
        AcceptInvitationRequest request)
    {
        var user = await userManager.FindByEmailAsync(invite.Email);
        // Invitations never create accounts: only server administrators do (sign-up is disabled on purpose).
        if (user is null || user.IsDeleted)
        {
            return new InviteePreparationResult(null, BadRequest(new
            {
                message = "No account exists for this invitation. Ask a server administrator to invite you first."
            }));
        }

        if (await userManager.HasPasswordAsync(user))
        {
            return new InviteePreparationResult(null, BadRequest(new
            {
                message = "This invited account already has a password. Sign in with that account to accept the invitation."
            }));
        }

        var result = await userManager.AddPasswordAsync(user, request.Password);
        return result.Succeeded
            ? new InviteePreparationResult(user, null)
            : new InviteePreparationResult(null, BadRequest(new
            {
                message = "Failed to set password",
                errors = result.Errors.Select(error => error.Description)
            }));
    }

    private async Task EnsureProjectMembershipAsync(InvitationToken invite, ApplicationUser user)
    {
        var membership = await db.ProjectMembers.FirstOrDefaultAsync(
            member => member.ProjectId == invite.ProjectId && member.UserId == user.Id);
        if (membership is null)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = invite.ProjectId,
                UserId = user.Id,
                Role = ParseInvitationRole(invite.Role),
                JoinedAt = DateTime.UtcNow,
                InvitedBy = invite.InvitedBy,
                IsActive = true
            });
            return;
        }

        if (!membership.IsActive)
        {
            membership.IsActive = true;
            membership.Role = ParseInvitationRole(invite.Role);
            membership.InvitedBy = invite.InvitedBy;
            membership.JoinedAt = DateTime.UtcNow;
        }
    }

    private static ProjectRole ParseInvitationRole(string role)
        => Enum.TryParse<ProjectRole>(role, out var parsedRole) ? parsedRole : ProjectRole.Viewer;

    private sealed record InviteePreparationResult(ApplicationUser? User, IActionResult? Error);
}
