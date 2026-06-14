---
name: Vue List Management Patterns
description: Best practices for managing lists in Vue components. Learn when to reload lists from the server vs. updating them dynamically on the client side for better UX and reliability.
tags: ["vue", "patterns", "lists", "ui-updates", "performance"]
---

# SKILL: Vue List Management Patterns

## Problem Statement

When performing actions on list items (create, update, delete, link, unlink), developers often reload the entire list from the server. This causes:

1. **Slow UI feedback** - Users see delays while waiting for server round-trip
2. **Dialog/modal not closing** - Parent component is waiting for reload to complete
3. **Network dependency** - If the reload fails silently, UI appears broken
4. **Poor user experience** - Feels unresponsive and slow
5. **Unnecessary server load** - Extra GET request after every mutation

### Real Example: The Problem

```typescript
// ❌ BAD: Dialog waits for full list reload
async function linkProject() {
  try {
    await organizationsService.linkProject(orgId, projectId)
    
    // This waits for full list reload - dialog stays open!
    await loadProjects()  // GET /api/organizations/{id}/projects
    
    showDialog.value = false  // Dialog closes AFTER reload (slow!)
  } catch (error) {
    // If loadProjects fails, dialog never closes
  }
}
```

**Problems:**
- Dialog doesn't close until full reload completes
- If reload fails, UI is stuck
- Extra network request
- Slower perceived performance

## Solution: Update Lists Dynamically

Instead of reloading from the server, update the list directly on the client side after the mutation succeeds.

```typescript
// ✅ GOOD: Immediate list update, instant dialog close
async function linkProject() {
  try {
    await organizationsService.linkProject(orgId, projectId)
    
    // Add project to list immediately (no server round-trip)
    projects.value.push({
      id: selectedProject.value.id,
      displayName: selectedProject.value.displayName,
      // ... other fields
    })
    
    // Close dialog immediately
    showDialog.value = false
    selectedProject.value = null
    
    // Show success feedback
    toast.add({ severity: 'success', detail: 'Project linked' })
  } catch (error) {
    // Error handling - toast already shown by service
  }
}
```

**Advantages:**
- Dialog closes instantly
- No dependency on server reload success
- Immediate visual feedback
- Better perceived performance
- Less server load

## Pattern 1: Adding Items to a List

**Scenario:** User creates a new item, it should appear in the list immediately.

```typescript
const items = ref<Item[]>([])

async function createItem(name: string) {
  try {
    // Create on server
    const newItem = await itemService.create({ name })
    
    // Add to local list immediately
    items.value.unshift({  // unshift adds to beginning
      id: newItem.id,
      name: newItem.name,
      createdAt: new Date().toISOString(),
    })
    
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `"${name}" added successfully`,
    })
  } catch (error) {
    // Service shows error toast
  }
}
```

**Key Points:**
- Use `unshift()` to add to the beginning (most recent first)
- Use `push()` to add to the end
- Include all fields needed by the template (avoid undefined values)

## Pattern 2: Removing Items from a List

**Scenario:** User deletes an item, it should disappear from the list immediately.

```typescript
const items = ref<Item[]>([])

async function deleteItem(itemId: string, itemName: string) {
  confirm.require({
    message: `Delete "${itemName}"?`,
    header: 'Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        // Delete on server
        await itemService.delete(itemId)
        
        // Remove from local list immediately
        items.value = items.value.filter(item => item.id !== itemId)
        
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `"${itemName}" removed successfully`,
        })
      } catch (error) {
        // Service shows error toast
      }
    },
  })
}
```

**Key Points:**
- Use `filter()` to remove items (creates new array, triggers Vue reactivity)
- Filter by unique ID to ensure correctness
- Show confirmation before deletion

## Pattern 3: Updating Items in a List

**Scenario:** User edits an item, it should update in the list immediately.

```typescript
const items = ref<Item[]>([])

async function updateItem(itemId: string, updates: Partial<Item>) {
  try {
    // Update on server
    const updated = await itemService.update(itemId, updates)
    
    // Update in local list
    const index = items.value.findIndex(item => item.id === itemId)
    if (index !== -1) {
      // Option 1: Merge the changes
      items.value[index] = { ...items.value[index], ...updated }
      
      // Option 2: Replace entire item
      // items.value[index] = updated
    }
    
    toast.add({
      severity: 'success',
      summary: 'Updated',
      detail: 'Item updated successfully',
    })
  } catch (error) {
    // Service shows error toast
  }
}
```

**Key Points:**
- Find the item by ID using `findIndex()`
- Use spread operator `...` to merge changes
- Important: Mutate the array element or reassign the entire array for reactivity
- Check if index exists to avoid errors

## Pattern 4: Toggling Item State (Link/Unlink)

**Scenario:** User links/unlinks items, they should move between lists.

### Moving Between Lists

```typescript
const organizationProjects = ref<Project[]>([])
const personalProjects = ref<Project[]>([])

async function linkProject(project: Project) {
  try {
    // Link on server
    await projectService.linkToOrganization(project.id)
    
    // Remove from personal list
    personalProjects.value = personalProjects.value.filter(
      p => p.id !== project.id
    )
    
    // Add to organization list
    organizationProjects.value.push(project)
    
    toast.add({
      severity: 'success',
      detail: `"${project.name}" linked successfully`,
    })
  } catch (error) {
    // Service shows error toast
  }
}

async function unlinkProject(project: Project) {
  try {
    // Unlink on server
    await projectService.unlinkFromOrganization(project.id)
    
    // Remove from organization list
    organizationProjects.value = organizationProjects.value.filter(
      p => p.id !== project.id
    )
    
    // Add back to personal list
    personalProjects.value.push(project)
    
    toast.add({
      severity: 'success',
      detail: `"${project.name}" unlinked successfully`,
    })
  } catch (error) {
    // Service shows error toast
  }
}
```

## Pattern 5: Complex Lists with Server-Side Sort/Filter

**When to Still Reload from Server:**

Sometimes you should reload the list instead of updating dynamically:

```typescript
// ✅ RELOAD when:
// 1. List has complex sorting (server-side)
// 2. List has complex filtering
// 3. You need to recalculate counts/aggregates
// 4. Item changes affect multiple lists

async function updateItemStatus(itemId: string, newStatus: string) {
  try {
    await itemService.updateStatus(itemId, newStatus)
    
    // Reload because:
    // - Sorting might change (updated items move positions)
    // - Filters might exclude this item
    // - Counts need recalculation
    await loadItems()  // Acceptable in this case
    
  } catch (error) {
    // Error handling
  }
}
```

**Decision Tree:**

```
Does the action need list reload?
├─ Item added/removed at end → NO, update dynamically
├─ Item properties changed (name, description) → NO, update dynamically
├─ Sorting will change (by date, count, etc.) → YES, reload
├─ Filtering will change (status, visibility) → YES, reload
├─ Aggregates affected (totals, counts) → YES, reload
└─ You're unsure → Reload (safe but slower)
```

## Best Practices

### 1. Always Close Dialogs/Modals First

```typescript
// ✅ GOOD: Close dialog immediately
async function createItem(name: string) {
  try {
    const newItem = await itemService.create({ name })
    items.value.push(newItem)
    
    // Close dialog right after API succeeds
    showDialog.value = false
    
    // Then show toast
    toast.add({ severity: 'success', detail: 'Created' })
  } catch (error) {
    // Keep dialog open so user can retry
  }
}
```

### 2. Use Confirmation Dialogs for Destructive Actions

```typescript
// ✅ GOOD: Confirm before delete
function deleteItem(itemId: string, itemName: string) {
  confirm.require({
    message: `Delete "${itemName}"?`,
    accept: async () => {
      try {
        await itemService.delete(itemId)
        items.value = items.value.filter(i => i.id !== itemId)
        toast.add({ severity: 'success', detail: 'Deleted' })
      } catch (error) {
        // Error already shown by service
      }
    },
  })
}
```

### 3. Maintain List Integrity

```typescript
// ✅ GOOD: Ensure list stays valid
async function updateItem(itemId: string, updates: any) {
  try {
    const updated = await itemService.update(itemId, updates)
    
    // Find and update
    const index = items.value.findIndex(i => i.id === itemId)
    if (index !== -1) {
      items.value[index] = updated
    }
    // If not found, you could reload or add it
    
  } catch (error) {
    // Error handling
  }
}
```

### 4. Handle Optimistic Updates (Advanced)

For even better UX, update the list BEFORE the API call (optimistic):

```typescript
// ✅ GOOD: Optimistic update
async function deleteItem(itemId: string) {
  // Remove immediately (optimistic)
  const originalItems = [...items.value]
  items.value = items.value.filter(i => i.id !== itemId)
  
  try {
    // Then confirm with server
    await itemService.delete(itemId)
    toast.add({ severity: 'success', detail: 'Deleted' })
  } catch (error) {
    // Restore on error
    items.value = originalItems
    toast.add({ severity: 'error', detail: 'Failed to delete' })
  }
}
```

## Common Mistakes to Avoid

### ❌ Mistake 1: Forgetting to Close Dialog

```typescript
// ❌ BAD: Dialog stays open
async function createItem(name: string) {
  await itemService.create({ name })
  // Forgot to close dialog!
  // showDialog.value = false
}
```

### ❌ Mistake 2: Not Updating List After Add

```typescript
// ❌ BAD: Item created but list doesn't show it
async function createItem(name: string) {
  const newItem = await itemService.create({ name })
  showDialog.value = false
  toast.add({ severity: 'success', detail: 'Created' })
  // Forgot to add to items.value!
}
```

### ❌ Mistake 3: Updating Wrong Index

```typescript
// ❌ BAD: Finds wrong item
async function updateItem(itemId: string) {
  const item = await itemService.get(itemId)
  items.value[0] = item  // Wrong! Updates first item!
  
  // ✅ GOOD:
  const index = items.value.findIndex(i => i.id === itemId)
  if (index !== -1) {
    items.value[index] = item
  }
}
```

### ❌ Mistake 4: Forgetting Confirmation for Destructive Actions

```typescript
// ❌ BAD: Delete without asking
async function deleteItem(itemId: string) {
  await itemService.delete(itemId)  // No confirmation!
  items.value = items.value.filter(i => i.id !== itemId)
}

// ✅ GOOD:
function deleteItem(itemId: string, name: string) {
  confirm.require({
    message: `Delete "${name}"?`,
    accept: async () => {
      await itemService.delete(itemId)
      items.value = items.value.filter(i => i.id !== itemId)
    },
  })
}
```

## Testing Checklist

- [ ] Dialog/modal closes immediately after action
- [ ] List updates without page reload
- [ ] Multiple actions work in sequence
- [ ] Error handling restores list to correct state
- [ ] Confirmation dialogs appear for destructive actions
- [ ] Toast messages show appropriate feedback
- [ ] No console errors after list operations
- [ ] List stays synchronized with server data

## Related Patterns

- **Pinia State Management** - Use store actions for shared list operations
- **Optimistic Updates** - Update UI before server confirmation
- **Undo/Redo** - Store previous state for rollback
- **Pagination** - Reload when filters/sorts change but not for simple add/remove
- **Real-time Updates** - Use WebSockets/SignalR to sync across users

## Resources

- [Vue 3 List Rendering](https://vuejs.org/guide/essentials/list.html)
- [Array Reactivity](https://vuejs.org/guide/extras/reactivity-in-depth.html#arrays)
- [Pinia Actions](https://pinia.vuejs.org/core-concepts/actions.html)
