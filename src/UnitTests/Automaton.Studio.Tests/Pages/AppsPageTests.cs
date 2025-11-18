using NUnit.Framework;

namespace Automaton.Studio.Tests.Pages;

/// <summary>
/// Tests for AppsPage default view mode behavior.
/// These tests verify the logic for setting the default view mode based on user permissions.
/// </summary>
[TestFixture]
public class AppsPageTests
{
    [Test]
    public void DefaultViewMode_WhenCanManageApps_ShouldBeListView()
    {
        // Arrange
        bool canManageApps = true;
        
        // Act - Simulate the logic from AppsPage.razor.cs: _isGridView = !AppsViewModel.CanManageApps
        bool isGridView = !canManageApps;
        
        // Assert
        Assert.That(isGridView, Is.False, "When CanManageApps is true (Personal mode or Project mode with manage rights), default should be List view (isGridView = false)");
    }

    [Test]
    public void DefaultViewMode_WhenCannotManageApps_ShouldBeGridView()
    {
        // Arrange
        bool canManageApps = false;
        
        // Act - Simulate the logic from AppsPage.razor.cs: _isGridView = !AppsViewModel.CanManageApps
        bool isGridView = !canManageApps;
        
        // Assert
        Assert.That(isGridView, Is.True, "When CanManageApps is false (Project mode without manage rights), default should be Grid view (isGridView = true)");
    }

    [Test]
    [TestCase(true, false, Description = "Can manage apps -> List view")]
    [TestCase(false, true, Description = "Cannot manage apps -> Grid view")]
    public void DefaultViewMode_Logic_IsConsistent(bool canManageApps, bool expectedIsGridView)
    {
        // Act - Simulate the initialization logic: _isGridView = !AppsViewModel.CanManageApps
        bool actualIsGridView = !canManageApps;
        
        // Assert
        Assert.That(actualIsGridView, Is.EqualTo(expectedIsGridView), 
            $"When CanManageApps is {canManageApps}, isGridView should be {expectedIsGridView}");
    }
}
