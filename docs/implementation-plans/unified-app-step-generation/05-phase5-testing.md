# Phase 5: Testing & Integration

## Overview

This phase focuses on end-to-end testing, integration verification, and polish to ensure the unified App + Backend Step generation workflow is production-ready.

**Estimated Effort:** 2-3 days

---

## Testing Strategy

### 1. Unit Tests

#### Custom Step Creation Tests

**Location:** `src/UnitTests/Automaton.Studio.Server.Tests/Services/AppStepsServiceTests.cs`

```csharp
using Automaton.Studio.Server.Services;
using Xunit;

namespace Automaton.Studio.Server.Tests.Services;

public class AppStepsServiceTests
{
    [Fact]
    public async Task LinkStepAsync_CreatesNewLink()
    {
        // Arrange
        var service = CreateService();
        var appId = Guid.NewGuid();
        var stepId = Guid.NewGuid();

        // Act
        await service.LinkStepAsync(appId, stepId);

        // Assert
        var linkedSteps = await service.GetLinkedStepsAsync(appId);
        Assert.Contains(linkedSteps, s => s.Id == stepId);
    }

    [Fact]
    public async Task LinkStepAsync_IdempotentForDuplicates()
    {
        // Arrange
        var service = CreateService();
        var appId = Guid.NewGuid();
        var stepId = Guid.NewGuid();

        // Act
        await service.LinkStepAsync(appId, stepId);
        await service.LinkStepAsync(appId, stepId); // Duplicate

        // Assert
        var linkedSteps = await service.GetLinkedStepsAsync(appId);
        Assert.Single(linkedSteps.Where(s => s.Id == stepId));
    }

    [Fact]
    public async Task UnlinkStepAsync_RemovesLink()
    {
        // Arrange
        var service = CreateService();
        var appId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        await service.LinkStepAsync(appId, stepId);

        // Act
        await service.UnlinkStepAsync(appId, stepId);

        // Assert
        var linkedSteps = await service.GetLinkedStepsAsync(appId);
        Assert.DoesNotContain(linkedSteps, s => s.Id == stepId);
    }

    [Fact]
    public async Task GetAvailableStepsAsync_ExcludesLinkedSteps()
    {
        // Arrange
        var service = CreateService();
        var appId = Guid.NewGuid();
        var linkedStepId = Guid.NewGuid();
        await service.LinkStepAsync(appId, linkedStepId);

        // Act
        var availableSteps = await service.GetAvailableStepsAsync(appId);

        // Assert
        Assert.DoesNotContain(availableSteps, s => s.Id == linkedStepId);
    }

    private AppStepsService CreateService()
    {
        // Create mock dependencies
        // Return configured service
        throw new NotImplementedException("Implement with mocks");
    }
}
```

#### Step Execution Tests

**Location:** `src/UnitTests/Automaton.Studio.Server.Tests/Services/AppsStepExecutionServiceTests.cs`

```csharp
public class AppsStepExecutionServiceTests
{
    [Fact]
    public async Task ExecuteStepForAppAsync_ResolvesStepByName()
    {
        // Test that step resolution by name works
    }

    [Fact]
    public async Task ExecuteStepForAppAsync_ResolvesStepByGuid()
    {
        // Test that step resolution by GUID works
    }

    [Fact]
    public async Task ExecuteStepForAppAsync_ReturnsErrorForUnknownStep()
    {
        // Test error handling for unknown step
    }

    [Fact]
    public async Task ExecuteStepForAppAsync_UsesAppConfiguredRunner()
    {
        // Test that app's ExecutionRunnerId is used when no runners specified
    }

    [Fact]
    public async Task ExecuteStepForAppAsync_UsesSpecifiedRunners()
    {
        // Test that specified runner names are respected
    }
}
```

---

### 2. Integration Tests

#### API Integration Tests

**Location:** `src/UnitTests/Automaton.Studio.Server.Tests/Controllers/AppsStepExecutionControllerTests.cs`

```csharp
public class AppsStepExecutionControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public AppsStepExecutionControllerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        this.client = factory.CreateClient();
    }

    [Fact]
    public async Task ExecuteStep_ReturnsNotFoundForUnpublishedApp()
    {
        // Arrange
        var request = new WebAppStepExecutionRequest { Inputs = new() };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/web-apps/nonexistent-app/steps/TestStep/execute",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteStep_ReturnsUnauthorizedForProtectedApp()
    {
        // Test auth requirement enforcement
    }

    [Fact]
    public async Task ExecuteStep_ExecutesSuccessfully()
    {
        // Test successful execution flow
    }

    [Fact]
    public async Task ListSteps_ReturnsLinkedSteps()
    {
        // Test step listing endpoint
    }
}
```

---

### 3. End-to-End Test Scenarios

#### Scenario 1: Complete App + Step Generation

```gherkin
Feature: Unified App Generation
  As a user
  I want to describe an app with backend needs
  So that the AI creates everything for me

  Scenario: Generate email form with SendGrid integration
    Given I am on the App designer page
    When I type "Create a contact form that sends emails via SendGrid"
    And I click Generate
    Then a new Custom Step "SendEmailStep" should be created
    And the step should have input variables: api_key, to_email, subject, body
    And the step should have output variables: success, message_id, error_message
    And the app code should contain automatonApi.executeStep('SendEmailStep')
    And the step should be visible in the Backend Actions tab

  Scenario: Modify existing app with new backend action
    Given I have an existing app with linked steps
    When I type "Add a feature to export data as CSV"
    And I click Generate
    Then a new Custom Step "ExportCsvStep" should be created
    And the existing app code should be updated
    And both steps should be visible in the Backend Actions tab
```

#### Scenario 2: Preview and Execute

```gherkin
Feature: App Preview with Step Execution
  
  Scenario: Preview app and execute backend step
    Given I have an app with a linked SendEmailStep
    And the step has valid code
    And a runner is connected
    When I open the Preview tab
    And I fill in the form
    And I click Submit
    Then the step should execute on the runner
    And the result should be displayed in the app
```

#### Scenario 3: Backend Actions Management

```gherkin
Feature: Backend Actions Tab
  
  Scenario: Link existing step to app
    Given I am on the App designer
    And there are existing Custom Steps
    When I click "Link Step" in Backend Actions tab
    And I select an existing step
    And I confirm
    Then the step should appear in the linked steps list
    
  Scenario: Navigate to edit step
    Given I have an app with linked steps
    When I click "Edit" on a step in Backend Actions tab
    Then I should navigate to the Step designer page
    And the step should be loaded for editing
```

---

### 4. Manual Testing Checklist

#### App Designer Page

- [ ] Backend Actions tab is visible and accessible
- [ ] Empty state shows when no steps are linked
- [ ] Link Step dialog opens correctly
- [ ] Available steps are listed (excluding already linked)
- [ ] Search/filter works in Link Step dialog
- [ ] Linking a step updates the list immediately
- [ ] Unlinking a step removes it from the list
- [ ] Edit button navigates to Step designer
- [ ] Copy code button copies correct `executeStep` snippet

#### AI Generation Flow

- [ ] Simple frontend-only requests work as before
- [ ] Backend-requiring requests create Custom Steps
- [ ] Step names follow PascalCase convention
- [ ] Input/output variables are created correctly
- [ ] Sensitive variables are marked as IsSensitive
- [ ] Step code follows best practices (error handling, logging)
- [ ] Frontend code includes proper executeStep calls
- [ ] Loading states and error handling are included
- [ ] Steps are automatically linked to the app

#### Direct Step Execution API

- [ ] `executeStep` works from published app
- [ ] Step resolution by name works
- [ ] Step resolution by GUID works
- [ ] Input variables are passed correctly
- [ ] Output variables are returned correctly
- [ ] Logs are captured and returned
- [ ] Auth requirements are enforced
- [ ] Error responses are properly formatted

#### Cross-Feature Integration

- [ ] Steps created from App appear in Custom Steps list
- [ ] Steps can be edited independently in Step designer
- [ ] Changes in Step designer are reflected in App
- [ ] Deleting a step updates App's linked steps
- [ ] Runner selection works correctly

---

### 5. Performance Testing

#### Response Time Targets

| Operation | Target | Maximum |
|-----------|--------|---------|
| Load Backend Actions tab | < 500ms | 1s |
| Link Step dialog load | < 500ms | 1s |
| Link/Unlink step | < 200ms | 500ms |
| Step execution (local) | < 2s | 5s |
| AI generation (simple) | < 10s | 30s |
| AI generation (with steps) | < 30s | 60s |

#### Load Testing

```csharp
[Fact]
public async Task StepExecution_HandlesMultipleConcurrentRequests()
{
    // Test 10 concurrent step executions
    var tasks = Enumerable.Range(0, 10)
        .Select(_ => ExecuteStepAsync())
        .ToArray();

    var results = await Task.WhenAll(tasks);

    // All should complete successfully
    Assert.All(results, r => Assert.True(r.Success));
}
```

---

### 6. Error Handling Tests

#### Expected Error Scenarios

| Scenario | Expected Behavior |
|----------|-------------------|
| Step not found | 404 response with clear message |
| App not published | 404 response (don't reveal existence) |
| Auth required but not provided | 401 response |
| Runner not connected | Error with instructions |
| Step execution fails | Error with step logs |
| Invalid inputs | 400 response with validation errors |

```csharp
[Theory]
[InlineData("nonexistent-step", HttpStatusCode.NotFound)]
[InlineData("", HttpStatusCode.NotFound)]
public async Task ExecuteStep_ReturnsCorrectErrorForInvalidStep(
    string stepIdentifier, 
    HttpStatusCode expected)
{
    var response = await client.PostAsJsonAsync(
        $"/api/web-apps/test-app/steps/{stepIdentifier}/execute",
        new WebAppStepExecutionRequest());

    Assert.Equal(expected, response.StatusCode);
}
```

---

### 7. Security Testing

#### Security Checklist

- [ ] Users can only access their own apps
- [ ] Users can only link/execute their own steps
- [ ] Published apps respect RequiresAuthentication flag
- [ ] Step code is never exposed in API responses
- [ ] Sensitive variables are not logged
- [ ] Rate limiting is applied to execution endpoints
- [ ] Input sanitization prevents injection attacks

---

### 8. Documentation Verification

Ensure all user-facing documentation is updated:

- [ ] `AGENTS.md` mentions new capabilities
- [ ] `wwwroot/docs/user-guide/web-apps.md` updated
- [ ] API reference includes new endpoints
- [ ] Example code snippets work correctly

---

## Rollout Plan

### Phase 1: Internal Testing (Day 1)
- Deploy to development environment
- Run automated test suite
- Manual testing by development team

### Phase 2: Beta Testing (Day 2)
- Deploy to staging environment
- Enable for beta users
- Collect feedback

### Phase 3: Production Release (Day 3)
- Final code review
- Deploy to production
- Monitor for issues
- Enable feature flag for all users

---

## Monitoring & Observability

### Metrics to Track

```csharp
// Add metrics for step execution
public class StepExecutionMetrics
{
    private readonly Counter<long> stepExecutionsTotal;
    private readonly Histogram<double> stepExecutionDuration;

    public void RecordExecution(string stepName, bool success, double durationMs)
    {
        stepExecutionsTotal.Add(1, 
            new("step_name", stepName),
            new("success", success));
        
        stepExecutionDuration.Record(durationMs,
            new("step_name", stepName));
    }
}
```

### Alerts to Configure

| Alert | Condition | Action |
|-------|-----------|--------|
| High error rate | > 10% failures in 5min | Page on-call |
| Slow executions | p95 > 10s | Investigate |
| Step creation failures | Any | Log for review |
| Auth bypass attempts | Unauthorized access patterns | Security review |

---

## Rollback Plan

If critical issues are found:

1. **Feature Flag**: Disable unified generation, fall back to separate App/Step generation
2. **API Rollback**: Revert `AppsStepExecutionController` changes
3. **UI Rollback**: Hide Backend Actions tab
4. **Data**: App-Step links can remain (no data loss)

```csharp
// Feature flag example
if (featureFlags.IsEnabled("unified-app-step-generation"))
{
    // New unified flow
}
else
{
    // Original separate flow
}
```

---

## Success Criteria

The feature is considered successful when:

1. **Functionality**: All test scenarios pass
2. **Performance**: Response times meet targets
3. **Reliability**: < 1% error rate in production
4. **User Adoption**: 50%+ of new apps use unified generation
5. **User Satisfaction**: Positive feedback on simplification

---

## Next Steps After Testing

Once testing is complete and successful:

1. Update [06-data-models.md](./06-data-models.md) with any schema changes
2. Update [07-api-reference.md](./07-api-reference.md) with final API specs
3. Create user documentation and tutorials
4. Plan for future enhancements (template marketplace, etc.)
