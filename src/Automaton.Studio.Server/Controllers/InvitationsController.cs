using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[Route("api/invitations")]
public class InvitationsController : ControllerBase
{
    private readonly InvitationTokenService invitationService;
    private readonly ApplicationDbContext db;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly UserContextService userContextService;

    public InvitationsController(
        InvitationTokenService invitationService,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        UserContextService userContextService)
    {
        this.invitationService = invitationService;
        this.db = db;
        this.userManager = userManager;
        this.signInManager = signInManager;
        this.userContextService = userContextService;
    }

    [HttpGet("validate/{token}")]
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
    [HttpPost("accept/{token}")]
    [Authorize]
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
            existing.Role = Enum.TryParse<Automaton.Studio.Server.Enums.ProjectRole>(invite.Role, out var r) ? r : Automaton.Studio.Server.Enums.ProjectRole.Viewer;
            existing.InvitedBy = invite.InvitedBy;
            existing.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = invite.ProjectId,
                UserId = user.Id,
                Role = Enum.TryParse<Automaton.Studio.Server.Enums.ProjectRole>(invite.Role, out var r2) ? r2 : Automaton.Studio.Server.Enums.ProjectRole.Viewer,
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

    // Combined accept + setup for unauthenticated users
    [HttpPost("accept/{token}/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> Complete(string token, [FromBody] AcceptInvitationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Password))
            return BadRequest(new { message = "Password is required" });

        var invite = await invitationService.ValidateInvitationToken(token);
        if (invite == null) return BadRequest(new { message = "Invalid or expired token" });

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == invite.ProjectId);
        if (project == null) return BadRequest(new { message = "Project not found" });

        var user = await userManager.FindByEmailAsync(invite.Email);

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = invite.Email,
                Email = invite.Email,
                Name = string.IsNullOrWhiteSpace(request.Name) ? invite.Email : request.Name,
                EmailConfirmed = true,
                IsEnterpriseEnabled = true
            };

            var createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return BadRequest(new { message = "Failed to create user", errors = createResult.Errors.Select(e => e.Description) });
            }

            user = await userManager.FindByEmailAsync(invite.Email);
        }
        else
        {
            var hasPassword = await userManager.HasPasswordAsync(user);
            if (!hasPassword)
            {
                var addResult = await userManager.AddPasswordAsync(user, request.Password);
                if (!addResult.Succeeded)
                {
                    return BadRequest(new { message = "Failed to set password", errors = addResult.Errors.Select(e => e.Description) });
                }
            }
            else
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await userManager.ResetPasswordAsync(user, resetToken, request.Password);
                if (!resetResult.Succeeded)
                {
                    return BadRequest(new { message = "Failed to reset password", errors = resetResult.Errors.Select(e => e.Description) });
                }
            }
        }

        var existingMembership = await db.ProjectMembers.FirstOrDefaultAsync(pm => pm.ProjectId == invite.ProjectId && pm.UserId == user.Id);
        if (existingMembership != null)
        {
            if (!existingMembership.IsActive)
            {
                existingMembership.IsActive = true;
                existingMembership.Role = Enum.TryParse<Automaton.Studio.Server.Enums.ProjectRole>(invite.Role, out var rr) ? rr : Automaton.Studio.Server.Enums.ProjectRole.Viewer;
                existingMembership.InvitedBy = invite.InvitedBy;
                existingMembership.JoinedAt = DateTime.UtcNow;
            }
        }
        else
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = invite.ProjectId,
                UserId = user.Id,
                Role = Enum.TryParse<Automaton.Studio.Server.Enums.ProjectRole>(invite.Role, out var rr2) ? rr2 : Automaton.Studio.Server.Enums.ProjectRole.Viewer,
                JoinedAt = DateTime.UtcNow,
                InvitedBy = invite.InvitedBy,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();

        await invitationService.ConsumeInvitationToken(token);

        await signInManager.SignInAsync(user, isPersistent: false);

        return Ok(new { message = "Invitation accepted and account setup complete", redirect = "/projects" });
    }
}