# Testing Patterns

Testing patterns and code examples for both frontend (Vue.js + Vitest) and backend (.NET) test suites.

## Frontend Testing (Vue.js + Vitest)

### Test Structure

**Location**: Tests colocated with components in `__tests__/` folders or `.spec.ts` files

**Naming Convention**: `ComponentName.spec.ts` or `functionName.spec.ts`

### Component Unit Test Example

```typescript
// src/features/flows/components/__tests__/FlowCard.spec.ts
import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import FlowCard from '../FlowCard.vue';
import type { Flow } from '../../types/flow.types';

describe('FlowCard', () => {
  it('renders flow name and status', () => {
    const flow: Flow = { 
      id: '1', 
      name: 'Test Flow', 
      status: 'active',
      createdAt: '2025-01-01T00:00:00Z'
    };
    
    const wrapper = mount(FlowCard, { 
      props: { flow } 
    });
    
    expect(wrapper.text()).toContain('Test Flow');
    expect(wrapper.text()).toContain('active');
  });

  it('emits delete event when delete button clicked', async () => {
    const flow: Flow = { 
      id: '1', 
      name: 'Test Flow', 
      status: 'active',
      createdAt: '2025-01-01T00:00:00Z'
    };
    
    const wrapper = mount(FlowCard, { 
      props: { flow } 
    });
    
    await wrapper.find('[data-testid="delete-button"]').trigger('click');
    
    expect(wrapper.emitted('delete')).toBeTruthy();
    expect(wrapper.emitted('delete')?.[0]).toEqual([flow.id]);
  });

  it('displays formatted date', () => {
    const flow: Flow = { 
      id: '1', 
      name: 'Test Flow', 
      status: 'active',
      createdAt: '2025-01-15T12:30:00Z'
    };
    
    const wrapper = mount(FlowCard, { 
      props: { flow } 
    });
    
    expect(wrapper.text()).toContain('Jan 15, 2025');
  });
});
```

### Service/API Test Example

```typescript
// src/features/flows/services/__tests__/flows.service.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flowsService } from '../flows.service';
import apiClient from '@/shared/services/api/axios-instance';

// Mock axios
vi.mock('@/shared/services/api/axios-instance');

describe('FlowsService', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches all flows', async () => {
    const mockFlows = [
      { id: '1', name: 'Flow 1', status: 'active' },
      { id: '2', name: 'Flow 2', status: 'inactive' }
    ];

    vi.mocked(apiClient.get).mockResolvedValue({ data: mockFlows });

    const result = await flowsService.getAll();

    expect(apiClient.get).toHaveBeenCalledWith('/api/flows');
    expect(result).toEqual(mockFlows);
  });

  it('creates a new flow', async () => {
    const newFlow = { name: 'New Flow', description: 'Test' };
    const createdFlow = { id: '3', ...newFlow, status: 'draft' };

    vi.mocked(apiClient.post).mockResolvedValue({ data: createdFlow });

    const result = await flowsService.create(newFlow);

    expect(apiClient.post).toHaveBeenCalledWith('/api/flows', newFlow);
    expect(result).toEqual(createdFlow);
  });

  it('handles API errors', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('Network error'));

    await expect(flowsService.getAll()).rejects.toThrow('Network error');
  });
});
```

### Composable Test Example

```typescript
// src/features/flows/composables/__tests__/useFlows.spec.ts
import { describe, it, expect, vi } from 'vitest';
import { useFlows } from '../useFlows';
import { flowsService } from '../../services/flows.service';

vi.mock('../../services/flows.service');

describe('useFlows', () => {
  it('loads flows on mount', async () => {
    const mockFlows = [{ id: '1', name: 'Test Flow' }];
    vi.mocked(flowsService.getAll).mockResolvedValue(mockFlows);

    const { flows, loadFlows } = useFlows();
    
    await loadFlows();

    expect(flows.value).toEqual(mockFlows);
  });

  it('sets loading state correctly', async () => {
    vi.mocked(flowsService.getAll).mockResolvedValue([]);

    const { loading, loadFlows } = useFlows();
    
    expect(loading.value).toBe(false);
    
    const promise = loadFlows();
    expect(loading.value).toBe(true);
    
    await promise;
    expect(loading.value).toBe(false);
  });
});
```

### Running Frontend Tests

```bash
# Run all tests
npm run test

# Run tests in watch mode
npm run test:watch

# Generate coverage report
npm run test:coverage

# Run specific test file
npm run test flows.service.spec.ts

# Run tests matching pattern
npm run test -- --grep="FlowCard"
```

### Frontend Test Types

- **Unit Tests**: Components, composables, services, stores
- **Integration Tests**: Feature workflows with mocked API
- **E2E Tests** (Playwright): Critical user flows end-to-end

## Backend Testing (.NET)

### Test Projects

- `Automaton.Core.Tests` - Workflow engine and step execution
- `Automaton.Studio.Server.Tests` - API controllers and services
- `Automaton.Runner.Tests` - Runner SignalR client and flow execution

### Controller Test Example

```csharp
// src/UnitTests/Automaton.Studio.Server.Tests/Controllers/FlowsControllerTests.cs
using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Automaton.Studio.Server.Controllers;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.DTOs;

public class FlowsControllerTests
{
    private readonly Mock<IFlowsService> _mockFlowsService;
    private readonly FlowsController _controller;

    public FlowsControllerTests()
    {
        _mockFlowsService = new Mock<IFlowsService>();
        _controller = new FlowsController(_mockFlowsService.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOkResult_WithFlows()
    {
        // Arrange
        var mockFlows = new List<FlowDto>
        {
            new FlowDto { Id = Guid.NewGuid(), Name = "Flow 1" },
            new FlowDto { Id = Guid.NewGuid(), Name = "Flow 2" }
        };
        _mockFlowsService.Setup(s => s.GetAllAsync()).ReturnsAsync(mockFlows);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var flows = Assert.IsAssignableFrom<List<FlowDto>>(okResult.Value);
        Assert.Equal(2, flows.Count);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenFlowDoesNotExist()
    {
        // Arrange
        var flowId = Guid.NewGuid();
        _mockFlowsService.Setup(s => s.GetByIdAsync(flowId)).ReturnsAsync((FlowDto?)null);

        // Act
        var result = await _controller.GetById(flowId);

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WithFlow()
    {
        // Arrange
        var request = new CreateFlowRequest { Name = "New Flow" };
        var createdFlow = new FlowDto { Id = Guid.NewGuid(), Name = "New Flow" };
        _mockFlowsService.Setup(s => s.CreateAsync(request)).ReturnsAsync(createdFlow);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var flow = Assert.IsType<FlowDto>(createdResult.Value);
        Assert.Equal("New Flow", flow.Name);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenSuccessful()
    {
        // Arrange
        var flowId = Guid.NewGuid();
        _mockFlowsService.Setup(s => s.DeleteAsync(flowId)).ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(flowId);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }
}
```

### Service Test with In-Memory Database

```csharp
// src/UnitTests/Automaton.Studio.Server.Tests/Services/FlowsServiceTests.cs
using Xunit;
using Microsoft.EntityFrameworkCore;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Entities;

public class FlowsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly FlowsService _service;

    public FlowsServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new FlowsService(_context);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllFlows()
    {
        // Arrange
        _context.Flows.AddRange(
            new Flow { Id = Guid.NewGuid(), Name = "Flow 1" },
            new Flow { Id = Guid.NewGuid(), Name = "Flow 2" }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task CreateAsync_AddsFlowToDatabase()
    {
        // Arrange
        var request = new CreateFlowRequest { Name = "New Flow", Description = "Test" };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("New Flow", result.Name);
        Assert.Equal(1, await _context.Flows.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_RemovesFlowFromDatabase()
    {
        // Arrange
        var flow = new Flow { Id = Guid.NewGuid(), Name = "Test Flow" };
        _context.Flows.Add(flow);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DeleteAsync(flow.Id);

        // Assert
        Assert.True(result);
        Assert.Equal(0, await _context.Flows.CountAsync());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
```

### Integration Test with WebApplicationFactory

```csharp
// src/UnitTests/Automaton.Studio.Server.Tests/Integration/FlowsApiTests.cs
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using Automaton.Studio.Server;
using Automaton.Studio.Server.DTOs;

public class FlowsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public FlowsApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        // Add authorization header if needed
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer <test-token>");
    }

    [Fact]
    public async Task GetFlows_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client.GetAsync("/api/flows");

        // Assert
        response.EnsureSuccessStatusCode();
        var flows = await response.Content.ReadFromJsonAsync<List<FlowDto>>();
        Assert.NotNull(flows);
    }

    [Fact]
    public async Task CreateFlow_ReturnsCreatedFlow()
    {
        // Arrange
        var request = new CreateFlowRequest { Name = "Test Flow" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/flows", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var flow = await response.Content.ReadFromJsonAsync<FlowDto>();
        Assert.NotNull(flow);
        Assert.Equal("Test Flow", flow.Name);
    }
}
```

### Running Backend Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test src/UnitTests/Automaton.Core.Tests

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test class
dotnet test --filter FullyQualifiedName~FlowsControllerTests

# Run tests matching pattern
dotnet test --filter "Name~Create"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Backend Test Patterns

**Arrange-Act-Assert (AAA) Pattern:**
```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - Set up test data and mocks
    var mockData = new MockData();
    var service = new Service(mockData);

    // Act - Execute the method under test
    var result = await service.DoSomethingAsync();

    // Assert - Verify the expected outcome
    Assert.Equal(expectedValue, result);
}
```

**Mock SignalR Hub Context:**
```csharp
var mockClients = new Mock<IHubCallerClients>();
var mockClientProxy = new Mock<IClientProxy>();
mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

var mockHubContext = new Mock<IHubContext<RunnerHub>>();
mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);
```

## Test Coverage Targets

- **Business Logic**: 80%+ coverage
- **Controllers/Services**: 70%+ coverage
- **Overall Project**: 60%+ coverage

## Best Practices

### Frontend (Vitest)

1. **Mock external dependencies** - Use `vi.mock()` for API clients, services
2. **Test user interactions** - Use `trigger()` to simulate clicks, inputs
3. **Test component emissions** - Verify events are emitted correctly
4. **Use data-testid attributes** - Make components easier to query in tests
5. **Avoid testing implementation details** - Focus on behavior, not internals
6. **Keep tests isolated** - Each test should be independent

### Backend (.NET)

1. **Use in-memory database for service tests** - Fast and isolated
2. **Mock dependencies** - Use Moq for interfaces and services
3. **Test edge cases** - Null checks, validation errors, not found scenarios
4. **Use WebApplicationFactory for integration tests** - Test full HTTP pipeline
5. **Clean up resources** - Implement `IDisposable` for database contexts
6. **Follow AAA pattern** - Arrange-Act-Assert for clarity

## Common Pitfalls

❌ **Don't:**
- Test framework internals (Vue reactivity, EF change tracking)
- Write tests that depend on execution order
- Mock everything - test real integrations where possible
- Ignore async/await - always await async operations
- Skip error cases - test failures too

✅ **Do:**
- Test business logic thoroughly
- Mock external services (HTTP, database in unit tests)
- Use realistic test data
- Test both success and failure paths
- Keep tests fast and focused
- Name tests clearly: `MethodName_Scenario_ExpectedBehavior`

## Reference

- Vitest Docs: https://vitest.dev/
- Vue Test Utils: https://test-utils.vuejs.org/
- xUnit Docs: https://xunit.net/
- Moq Docs: https://github.com/moq/moq4
