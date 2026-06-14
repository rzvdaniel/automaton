---
name: primevue-global-components
description: Guide for avoiding duplicate PrimeVue service component instances in Vue.js feature components. Use this when working with PrimeVue components in the Automaton Vue.js frontend, especially when dealing with ConfirmDialog, Toast, or similar service-based components.
---

# SKILL: PrimeVue Global Components in Vue.js Projects

## Problem Statement

Importing and using PrimeVue service components like `ConfirmDialog` locally in feature components can cause multiple instances to render, resulting in unintended behavior such as dialogs opening multiple times. This occurs when the component is already registered globally at the application root.

## Solution

**Always use globally-registered PrimeVue service components.** These are defined at the application root and accessed via composition hooks rather than local imports.

### Why PrimeVue Dialogs Over Browser Dialogs

**Never use browser's native `confirm()` or `alert()`** – always use PrimeVue's `useConfirm()` hook instead:

| Feature | Browser `confirm()` | PrimeVue `useConfirm()` |
|---------|---|---|
| **Styling** | Plain, browser default | Theme-aware, matches app design |
| **Customization** | None | Headers, icons, button classes |
| **User Experience** | Blocks interaction | Non-blocking, async |
| **Accessibility** | Limited | Full ARIA support |
| **Consistency** | Varies by browser | Consistent across all browsers |

**Example of what to avoid:**
```typescript
// ❌ BAD: Browser dialog
if (!confirm('Delete this item?')) {
  return
}
```

**Use this instead:**
```typescript
// ✅ GOOD: PrimeVue styled dialog
confirm.require({
  message: 'Are you sure you want to delete this item?',
  header: 'Delete Item',
  icon: 'pi pi-exclamation-triangle',
  acceptClass: 'p-button-danger',
  accept: () => {
    // Handle deletion
  },
})
```

## Global Service Components

The following PrimeVue components are **service-based** and should NOT be imported locally:

| Component | Hook | Usage |
|-----------|------|-------|
| **ConfirmDialog** | `useConfirm()` | Confirmation prompts |
| **Toast** | `useToast()` | Notifications |
| **Sidebar** | `useSidebar()` | Off-canvas navigation |
| **Tooltip** | `v-tooltip` directive | Inline tooltips |

## ✅ Correct Pattern

**In feature components:**

```vue
<template>
  <div>
    <Button 
      label="Delete" 
      severity="danger"
      @click="handleDelete"
    />
    <!-- DO NOT include ConfirmDialog here -->
  </div>
</template>

<script setup lang="ts">
import { useConfirm } from 'primevue/useconfirm';

const confirm = useConfirm();

function handleDelete() {
  confirm.require({
    message: 'Are you sure?',
    header: 'Confirm',
    accept: () => {
      // Handle deletion
    },
  });
}
</script>
```

## ❌ Incorrect Pattern

```vue
<template>
  <div>
    <Button label="Delete" @click="handleDelete" />
    <ConfirmDialog />  <!-- ❌ DO NOT IMPORT LOCALLY -->
  </div>
</template>

<script setup lang="ts">
import { useConfirm } from 'primevue/useconfirm';
import ConfirmDialog from 'primevue/confirmdialog';  <!-- ❌ WRONG -->
</script>
```

## Where Global Components Are Defined

**File:** `src/automaton-studio-vue/src/App.vue` or `src/automaton-studio-vue/src/main.ts`

Global components are typically registered in the main app setup:

```typescript
// main.ts or App.vue setup
import ConfirmDialog from 'primevue/confirmdialog';
import Toast from 'primevue/toast';

// Register globally (done once at app root only)
app.component('ConfirmDialog', ConfirmDialog);
app.component('Toast', Toast);

// Add to template in App.vue:
// <ConfirmDialog />
// <Toast />
```

## Rules for Feature Components

1. **Never import service components locally** – use hooks instead
2. **Only import UI components** (Button, Card, DataTable, Select, etc.)
3. **Use composition hooks** for dialogs and notifications:
   - `useConfirm()` → `confirm.require()`
   - `useToast()` → `toast.add()`
4. **Do not render component instances** from service components in feature templates

## Local vs. Global Components

| Type | Import Locally? | How to Use |
|------|---|---|
| **Service Components** (ConfirmDialog, Toast, Sidebar) | ❌ NO | Use composition hook |
| **UI Components** (Button, Card, DataTable, Select, Dialog) | ✅ YES | Import and use in template |

## Examples for This Project

### ✅ Correct: MembersTab.vue (After Fix)

```vue
<!-- Template -->
<Button 
  icon="pi pi-trash" 
  @click="handleRemove(data)"
/>

<!-- Script -->
import { useConfirm } from 'primevue/useconfirm';

const confirm = useConfirm();

function handleRemove(member: ProjectMemberDto) {
  confirm.require({
    message: `Remove ${member.userName}?`,
    header: 'Remove Member',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      await store.removeMember(props.projectId, member.userId);
    },
  });
}
```

### ✅ Correct: Using ConfirmDialog with Styling

```typescript
import { useConfirm } from 'primevue/useconfirm';

const confirm = useConfirm();

function handleDelete(itemName: string) {
  confirm.require({
    message: `Are you sure you want to delete "${itemName}"?`,
    header: 'Delete Item',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',  // Red danger styling
    accept: async () => {
      // Perform deletion
      await deleteItem();
      toast.add({
        severity: 'success',
        summary: 'Success',
        detail: 'Item deleted successfully',
        life: 3000,
      });
    },
  });
}
```

**Key Options:**
- `header` - Dialog title
- `message` - Confirmation question
- `icon` - PrimeVue icon class
- `acceptClass` - CSS class for accept button (use `'p-button-danger'` for destructive actions)
- `accept` - Callback function when user confirms

### ✅ Correct: Using Toast

```typescript
import { useToast } from 'primevue/usetoast';

const toast = useToast();

function showSuccess() {
  toast.add({
    severity: 'success',
    summary: 'Success',
    detail: 'Operation completed',
    life: 3000,
  });
}
```

## AI Model Guidance

When working with PrimeVue components in this Vue.js project:

1. **Check if component has a hook** → Use the hook (e.g., `useConfirm()`)
2. **Check if component is service-based** → Never import locally
3. **For UI components** → Safe to import locally in feature components
4. **When in doubt** → Check if `App.vue` already has a global instance

## Testing the Fix

After removing local instances:
- Dialog/Toast should still work (uses global instance)
- No multiple dialogs should appear
- All functionality remains unchanged

## Decision Tree

```
Is this a PrimeVue component?
├─ YES
│  └─ Does it have a composition hook (useConfirm, useToast)?
│     ├─ YES → Use the hook, DO NOT import locally
│     └─ NO → Safe to import locally (it's a UI component)
└─ NO → Proceed normally
```

---

## Search Input with Clear Button

Use PrimeVue's `IconField` + `InputIcon` to embed icons (including an interactive clear button) inside an `InputText`. This keeps sizing consistent and avoids the hover/size mismatch you get when overlaying an absolute-positioned `Button`.

### ✅ Correct Pattern

```vue
<template>
  <IconField>
    <InputIcon
      :class="searchQuery ? 'pi pi-times cursor-pointer' : 'pi pi-search'"
      @click="searchQuery ? searchQuery = '' : undefined"
    />
    <InputText v-model="searchQuery" placeholder="Search..." size="small" class="w-full" />
  </IconField>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import InputText from 'primevue/inputtext'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'

const searchQuery = ref('')
</script>
```

- When empty → shows 🔍 search icon (non-interactive)
- When populated → switches to ✕ icon; click clears the input
- No custom CSS needed; icon is naturally sized to the input

### ❌ Incorrect Pattern

```vue
<!-- DON'T: absolute-positioned Button overlay causes hover size issues -->
<div style="position: relative">
  <InputText v-model="searchQuery" style="padding-right: 2rem" />
  <Button
    v-if="searchQuery"
    icon="pi pi-times"
    style="position: absolute; right: 0.25rem"
    @click="searchQuery = ''"
  />
</div>
```
