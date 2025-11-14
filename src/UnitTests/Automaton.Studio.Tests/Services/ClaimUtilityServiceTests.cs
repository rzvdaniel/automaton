using System.Security.Claims;
using NUnit.Framework;
using NSubstitute;
using FluentAssertions;
using Automaton.Studio.Services;

namespace Automaton.Studio.Tests.Services;

[TestFixture]
public class ClaimUtilityServiceTests
{
    private IClaimUtilityService _claimUtilityService;

    [SetUp]
    public void SetUp()
    {
        _claimUtilityService = new ClaimUtilityService();
    }

    [Test]
    public void GetUserEmail_WithEmailClaim_ReturnsEmail()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new Claim("email", "user@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserEmail(principal);

        // Assert
        result.Should().Be("user@example.com");
    }

    [Test]
    public void GetUserEmail_WithUniqueName_ReturnsEmail()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new Claim("unique_name", "user@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserEmail(principal);

        // Assert
        result.Should().Be("user@example.com");
    }

    [Test]
    public void GetUserEmail_WithMultipleClaims_ReturnsPriority()
    {
        // Arrange - email claim has priority over unique_name
        var claims = new List<Claim>
        {
            new Claim("unique_name", "unique@example.com"),
            new Claim("email", "primary@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserEmail(principal);

        // Assert - should return email claim (higher priority)
        result.Should().Be("primary@example.com");
    }

    [Test]
    public void GetUserEmail_WithClaimTypesEmail_ReturnsEmail()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, "user@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserEmail(principal);

        // Assert
        result.Should().Be("user@example.com");
    }

    [Test]
    public void GetUserEmail_WithNameIdentifier_ReturnsValue()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "user@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserEmail(principal);

        // Assert
        result.Should().Be("user@example.com");
    }

    [Test]
    public void GetUserEmail_WithNullPrincipal_ReturnsNull()
    {
        // Act
        var result = _claimUtilityService.GetUserEmail(null!);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void GetUserEmail_WithNoClaims_ReturnsNull()
    {
        // Arrange
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserEmail(principal);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void GetUserId_WithUidClaim_ReturnsId()
    {
        // Arrange
        var userId = "08de22a6-fbfc-407e-8c32-60d25b332599";
        var claims = new List<Claim>
        {
            new Claim("uid", userId)
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserId(principal);

        // Assert
        result.Should().Be(userId);
    }

    [Test]
    public void GetUserId_WithNameIdentifier_ReturnsId()
    {
        // Arrange
        var userId = "08de22a6-fbfc-407e-8c32-60d25b332599";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserId(principal);

        // Assert
        result.Should().Be(userId);
    }

    [Test]
    public void GetUserId_WithSubClaim_ReturnsId()
    {
        // Arrange
        var userId = "08de22a6-fbfc-407e-8c32-60d25b332599";
        var claims = new List<Claim>
        {
            new Claim("sub", userId)
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserId(principal);

        // Assert
        result.Should().Be(userId);
    }

    [Test]
    public void GetUserId_WithMultipleClaims_ReturnsPriority()
    {
        // Arrange - uid claim has priority over sub
        var uidValue = "uid-value";
        var subValue = "sub-value";
        var claims = new List<Claim>
        {
            new Claim("sub", subValue),
            new Claim("uid", uidValue)
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserId(principal);

        // Assert - should return uid claim (higher priority)
        result.Should().Be(uidValue);
    }

    [Test]
    public void GetUserId_WithNullPrincipal_ReturnsNull()
    {
        // Act
        var result = _claimUtilityService.GetUserId(null!);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void GetUserName_WithNameClaim_ReturnsName()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "John Doe")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserName(principal);

        // Assert
        result.Should().Be("John Doe");
    }

    [Test]
    public void GetUserName_WithCustomNameClaim_ReturnsName()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new Claim("name", "Jane Smith")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserName(principal);

        // Assert
        result.Should().Be("Jane Smith");
    }

    [Test]
    public void GetUserName_FallsBackToEmail_ReturnsEmail()
    {
        // Arrange - no name claim, should fallback to email
        var claims = new List<Claim>
        {
            new Claim("email", "user@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = _claimUtilityService.GetUserName(principal);

        // Assert
        result.Should().Be("user@example.com");
    }

    [Test]
    public void GetUserName_WithNullPrincipal_ReturnsNull()
    {
        // Act
        var result = _claimUtilityService.GetUserName(null!);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void EmailsMatch_WithMatchingEmails_ReturnsTrue()
    {
        // Arrange
        var email1 = "user@example.com";
        var email2 = "user@example.com";

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void EmailsMatch_WithDifferentCase_ReturnsTrue()
    {
        // Arrange
        var email1 = "User@Example.com";
        var email2 = "user@example.com";

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert - should be case-insensitive
        result.Should().BeTrue();
    }

    [Test]
    public void EmailsMatch_WithDifferentEmails_ReturnsFalse()
    {
        // Arrange
        var email1 = "user1@example.com";
        var email2 = "user2@example.com";

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EmailsMatch_WithNullEmail_ReturnsFalse()
    {
        // Arrange
        var email1 = "user@example.com";
        string? email2 = null;

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EmailsMatch_WithBothNullEmails_ReturnsFalse()
    {
        // Arrange
        string? email1 = null;
        string? email2 = null;

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EmailsMatch_WithEmptyString_ReturnsFalse()
    {
        // Arrange
        var email1 = "user@example.com";
        var email2 = string.Empty;

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EmailsMatch_WithWhitespace_ReturnsFalse()
    {
        // Arrange
        var email1 = "user@example.com";
        var email2 = "   ";

        // Act
        var result = _claimUtilityService.EmailsMatch(email1, email2);

        // Assert
        result.Should().BeFalse();
    }
}
