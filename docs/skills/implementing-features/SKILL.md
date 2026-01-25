# Implementing New Features

Step-by-step guide for implementing a new feature across the full stack (ASP.NET Core backend + Vue.js frontend).

## Feature Implementation Workflow

### 1. Backend (ASP.NET Core)

Create controller in `src/Automaton.Studio.Server/Controllers/YourFeatureController.cs`:

```csharp
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class YourFeatureController : ControllerBase
{
    private readonly IYourFeatureService _service;

    public YourFeatureController(IYourFeatureService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<YourFeatureDto>>> GetAll()
    {
        var items = await _service.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<YourFeatureDto>> GetById(Guid id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<YourFeatureDto>> Create([FromBody] CreateYourFeatureRequest request)
    {
        var item = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<YourFeatureDto>> Update(Guid id, [FromBody] UpdateYourFeatureRequest request)
    {
        var item = await _service.UpdateAsync(id, request);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var success = await _service.DeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }
}
```

**Controller Attributes:**
- `[Route("api/[controller]")]` - Sets base route to `/api/yourfeature`
- `[ApiController]` - Enables automatic model validation and binding
- `[Authorize]` - Requires JWT authentication for all endpoints

**Common HTTP Patterns:**
- **List:** `GET /api/resource` (optional query params)
- **Get Single:** `GET /api/resource/{id}`
- **Create:** `POST /api/resource` (body: CreateRequest)
- **Update:** `PUT /api/resource/{id}` (body: UpdateRequest)
- **Delete:** `DELETE /api/resource/{id}`

**HTTP Status Codes:**
- 200 OK, 201 Created, 204 No Content - Success
- 400 Bad Request - Validation error
- 401 Unauthorized - Auth required
- 403 Forbidden - Insufficient permissions
- 404 Not Found - Resource not found
- 500 Internal Server Error - Server error

### 2. Frontend Feature Structure

Create feature folder with standard layout:

```
src/automaton-studio-vue/src/features/your-feature/
├── components/       # YourFeatureCard.vue, YourFeatureDialog.vue
├── services/         # yourFeature.service.ts
├── types/            # yourFeature.types.ts
└── views/            # YourFeatureView.vue
```

### 3. TypeScript Types

Create `types/yourFeature.types.ts`:

```typescript
// Match backend DTOs exactly (property names, types, optionality)
export interface YourFeature {
  id: string;
  name: string;
  description?: string;
  status: YourFeatureStatus;
  createdAt: string;
  updatedAt: string;
}

// Use const objects instead of enums
export const YourFeatureStatus = {
  Active: 1,
  Inactive: 2,
  Archived: 3
} as const;

export type YourFeatureStatusType = typeof YourFeatureStatus[keyof typeof YourFeatureStatus];

// Helper functions for UI display
export function getStatusSeverity(status: YourFeatureStatusType): string {
  switch (status) {
    case YourFeatureStatus.Active: return 'success';
    case YourFeatureStatus.Inactive: return 'warn';
    case YourFeatureStatus.Archived: return 'secondary';
    default: return 'info';
  }
}

export function getStatusLabel(status: YourFeatureStatusType): string {
  switch (status) {
    case YourFeatureStatus.Active: return 'Active';
    case YourFeatureStatus.Inactive: return 'Inactive';
    case YourFeatureStatus.Archived: return 'Archived';
    default: return 'Unknown';
  }
}

export function formatDate(dateString: string): string {
  const date = new Date(dateString);
  return date.toLocaleDateString('en-US', { 
    year: 'numeric', 
    month: 'short', 
    day: 'numeric' 
  });
}
```

### 4. API Service

Create `services/yourFeature.service.ts`:

```typescript
import apiClient from '@/shared/services/api/axios-instance';
import type { YourFeature } from '../types/yourFeature.types';

class YourFeatureService {
  private readonly basePath = '/api/yourfeature';

  async getAll(): Promise<YourFeature[]> {
    const response = await apiClient.get<YourFeature[]>(this.basePath);
    return response.data;
  }

  async getById(id: string): Promise<YourFeature> {
    const response = await apiClient.get<YourFeature>(`${this.basePath}/${id}`);
    return response.data;
  }

  async create(data: Partial<YourFeature>): Promise<YourFeature> {
    const response = await apiClient.post<YourFeature>(this.basePath, data);
    return response.data;
  }

  async update(id: string, data: Partial<YourFeature>): Promise<YourFeature> {
    const response = await apiClient.put<YourFeature>(`${this.basePath}/${id}`, data);
    return response.data;
  }

  async delete(id: string): Promise<void> {
    await apiClient.delete(`${this.basePath}/${id}`);
  }
}

// Export singleton instance
export const yourFeatureService = new YourFeatureService();
```

**Notes:**
- Import `apiClient` from `@/shared/services/api/axios-instance`
- JWT tokens are automatically injected by interceptors
- Handle errors with try/catch in components
- Keep services stateless and focused on API calls

### 5. Vue Component

Create `views/YourFeatureView.vue`:

```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useToast } from 'primevue/usetoast';
import { yourFeatureService } from '../services/yourFeature.service';
import type { YourFeature } from '../types/yourFeature.types';
import { getStatusSeverity, getStatusLabel, formatDate } from '../types/yourFeature.types';

const toast = useToast();
const items = ref<YourFeature[]>([]);
const loading = ref(false);
const dialogVisible = ref(false);

onMounted(() => {
  loadItems();
});

async function loadItems() {
  loading.value = true;
  try {
    items.value = await yourFeatureService.getAll();
  } catch (error) {
    console.error('Failed to load items:', error);
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load items',
      life: 3000
    });
  } finally {
    loading.value = false;
  }
}

async function handleDelete(id: string) {
  try {
    await yourFeatureService.delete(id);
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Item deleted successfully',
      life: 3000
    });
    await loadItems();
  } catch (error) {
    console.error('Failed to delete item:', error);
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to delete item',
      life: 3000
    });
  }
}
</script>

<template>
  <div class="your-feature-view">
    <Card>
      <template #title>Your Feature</template>
      <template #content>
        <DataTable :value="items" :loading="loading">
          <Column field="name" header="Name" sortable />
          <Column field="status" header="Status">
            <template #body="slotProps">
              <Tag 
                :value="getStatusLabel(slotProps.data.status)" 
                :severity="getStatusSeverity(slotProps.data.status)" 
              />
            </template>
          </Column>
          <Column field="createdAt" header="Created">
            <template #body="slotProps">
              {{ formatDate(slotProps.data.createdAt) }}
            </template>
          </Column>
          <Column header="Actions">
            <template #body="slotProps">
              <Button 
                icon="pi pi-trash" 
                severity="danger" 
                text 
                @click="handleDelete(slotProps.data.id)" 
              />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.your-feature-view {
  /* No background - inherits from parent */
  padding: 1rem;
}
</style>
```

## Best Practices Checklist

- ✅ Match TypeScript types to C# DTOs exactly (property names, types, optionality)
- ✅ Use const objects instead of enums in TypeScript
- ✅ Add loading states for async operations
- ✅ Show toast notifications for user feedback (success, error, warning)
- ✅ Implement empty states with helpful messages
- ✅ Add confirmation dialogs for destructive actions
- ✅ Use computed properties to optimize template expressions
- ✅ Handle errors gracefully with try/catch
- ✅ Format dates consistently using helper functions
- ✅ Keep services stateless and focused on API calls
- ✅ Colocate feature code (types, services, components together)
- ✅ Never hardcode backgrounds - see `theme-aware-styling` skill

## Error Handling Pattern

```typescript
async function performAction() {
  try {
    const result = await yourFeatureService.create(data);
    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Action completed successfully',
      life: 3000
    });
    return result;
  } catch (error) {
    console.error('Action failed:', error);
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Action failed. Please try again.',
      life: 3000
    });
  }
}
```

## Reference Implementation

See API Keys feature in `src/automaton-studio-vue/src/features/settings/` for complete example demonstrating all patterns above.
