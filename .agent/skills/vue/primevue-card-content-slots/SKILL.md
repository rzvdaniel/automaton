---
name: primevue-card-content-slots
description: Guide for building PrimeVue Card-based UI blocks so content and actions render correctly using header/content/footer slots.
---

# SKILL: PrimeVue Card Content Slots

## Problem Statement

PrimeVue `Card` does **not** render body content from the default slot. If you place table rows, buttons, or other content directly inside the component, the card may render only the header while the body appears empty.

This causes cards to show a title (and maybe a badge) but no actions, tables, or text.

## Root Cause

`Card` expects named slots:
- `#header`
- `#content`
- `#footer`

Anything not placed in `#content` or `#footer` can be ignored or rendered inconsistently.

## ✅ Correct Pattern

Always place body content inside `#content`:

```vue
<Card>
  <template #header>
    <div class="card-header">
      <h3>Pending Invitations</h3>
      <span class="badge">{{ count }}</span>
    </div>
  </template>

  <template #content>
    <p class="description">Manage your invitations below.</p>

    <DataTable :value="pendingInvitations">
      <Column field="organizationName" header="Organization" />
      <Column header="Actions">
        <template #body="{ data }">
          <Button label="Accept" @click="acceptInvitation(data.token)" />
          <Button label="Decline" severity="secondary" />
        </template>
      </Column>
    </DataTable>
  </template>
</Card>
```

## ❌ Incorrect Pattern

```vue
<Card>
  <template #header>
    <h3>Pending Invitations</h3>
  </template>

  <!-- ❌ This can render as empty in PrimeVue Card -->
  <DataTable :value="pendingInvitations" />
</Card>
```

## Checklist Before Shipping

- [ ] Header is inside `#header`
- [ ] Body content is inside `#content`
- [ ] Actions belong inside `#content` or `#footer`
- [ ] Table buttons are visible and clickable
- [ ] Card renders correctly in Dashboard view

## Where to Use This

Use this pattern for any dashboard card or list card in:
- `src/features/dashboard/`
- `src/features/organizations/`
- `src/features/projects/`

## References

- [PrimeVue Card docs](https://primevue.org/card/)
