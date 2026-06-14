---
name: vue-theme-aware-styling
description: Guide for applying UI changes according to the light and dark themes.
---

# Theme-Aware Styling

Apply PrimeVue theme-aware CSS patterns to ensure components work in both light and dark modes.

## Core Principle

**CRITICAL: Never hardcode background colors.** Components must inherit backgrounds from parents or use theme-adaptive CSS variables.

## Common Mistakes

### ❌ DON'T - Hardcoded backgrounds break dark mode

```css
.my-component {
  background-color: var(--p-surface-0);   /* Always white - breaks dark mode */
  background-color: var(--p-surface-100); /* Always light gray - breaks dark mode */
  background-color: white;                /* Hardcoded - breaks dark mode */
  background-color: #ffffff;              /* Hardcoded - breaks dark mode */
}
```

## Correct Patterns

### ✅ Option 1: No background - inherit from parent (PREFERRED)

```css
.my-component {
  /* No background-color - inherits from themed parent */
  border: 1px solid var(--p-surface-200);
}
```

### ✅ Option 2: Use theme-adaptive variables

```css
.my-component {
  background-color: var(--p-surface-ground); /* Adapts to theme */
  color: var(--p-text-color);                /* Adapts to text */
  color: var(--p-text-muted-color);          /* For secondary text */
}
```

### ✅ Dark mode border adjustments (only borders, not backgrounds)

```css
/* Base styles - no background */
.my-header {
  border-bottom: 1px solid var(--p-surface-200);
}

/* Dark mode - only adjust border color */
:global(.app-dark) .my-header {
  border-color: var(--p-surface-700);
}
```

## Theme-Adaptive PrimeVue CSS Variables

### Safe Variables (Use These)

| Variable | Light Mode | Dark Mode | Use For |
|----------|-----------|-----------|---------|
| `--p-surface-ground` | Light bg | Dark bg | Page backgrounds |
| `--p-text-color` | Dark text | Light text | Primary text |
| `--p-text-muted-color` | Gray text | Light gray | Secondary/muted text |
| `--p-surface-hover` | Hover bg | Hover bg | Interactive hover states |
| `--p-primary-500` | Brand color | Brand color | Accent/brand elements |

### Unsafe Variables (AVOID for backgrounds)

- `--p-surface-0` (always white)
- `--p-surface-100` through `--p-surface-400` (always light shades)
- `--p-surface-800`, `--p-surface-900` (always dark shades)

## Component Hierarchy for Theme Inheritance

```
.app-dark (root)
  └── .app-editor-view (background: var(--p-surface-ground) / dark: var(--p-surface-900))
        └── .splitter-panel (no background - inherits)
              └── .file-panel (no background - inherits)
              └── .editor-panel (no background - inherits)
              └── .ai-sidebar (no background - inherits)
```

## Key Takeaways

1. **Never hardcode backgrounds** - omit `background-color` or use `--p-surface-ground`
2. **Prefer inheritance** - let components inherit from themed parents
3. **Only adjust borders in dark mode** - not backgrounds
4. **Use safe CSS variables** - avoid `--p-surface-0` through `--p-surface-400`
5. **Test in both themes** - verify light and dark modes before committing
