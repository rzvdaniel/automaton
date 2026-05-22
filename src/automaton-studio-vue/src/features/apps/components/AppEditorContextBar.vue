<script setup lang="ts">
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'

defineProps<{
  appName: string
  isEditingName: boolean
  editingName: string
  isPublished: boolean
}>()

const emit = defineEmits<{
  back: []
  'update:editingName': [value: string]
  startEditing: []
  saveName: []
  cancelEditing: []
}>()
</script>

<template>
  <div class="editor-context-bar">
    <div class="context-bar-left">
      <Button
        v-tooltip.bottom="'Back to Apps'"
        icon="pi pi-arrow-left"
        text
        rounded
        severity="secondary"
        aria-label="Back to Apps"
        @click="emit('back')"
      />

      <div class="app-identity">
        <div class="app-identity-row">
          <i class="pi pi-globe app-icon"></i>
          <span v-if="!isEditingName" class="app-name" @click="emit('startEditing')">
            {{ appName }}
          </span>
          <InputText
            v-else
            :model-value="editingName"
            class="app-name-input"
            @update:model-value="emit('update:editingName', $event)"
            @blur="emit('saveName')"
            @keyup.enter="emit('saveName')"
            @keyup.escape="emit('cancelEditing')"
          />
          <Tag v-if="isPublished" value="Published" severity="success" />
          <Tag v-else value="Draft" severity="warn" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.editor-context-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.5rem 1rem 0.25rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.context-bar-left {
  display: flex;
  align-items: center;
  min-width: 0;
  gap: 0.5rem;
}

.app-identity {
  min-width: 0;
}

.app-identity-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.app-icon {
  color: var(--p-primary-500);
  flex-shrink: 0;
}

.app-name {
  font-size: 1.125rem;
  font-weight: 600;
  cursor: pointer;
  color: var(--p-text-color);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-name:hover {
  text-decoration: underline;
}

.app-name-input {
  width: min(20rem, 100%);
  font-size: 1.125rem;
  font-weight: 600;
}

:global(.app-dark) .editor-context-bar {
  border-color: var(--p-surface-700);
}

@media (max-width: 768px) {
  .editor-context-bar {
    padding: 0.5rem 0.75rem 0.25rem;
  }
}
</style>
