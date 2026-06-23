<script setup lang="ts">
defineProps<{
  activeTab: 'build' | 'code' | 'backend-server' | 'ai' | 'logs' | 'settings'
  editorModeNoticeLabel: string | null | undefined
  editorModeNoticeText: string | null | undefined
  showsLocalLiveBanner: boolean
  showsSourceDeployBanner: boolean
  deployAppStatusMessage: string | null | undefined
  deployStatusMessage: string | null | undefined
}>()

const emit = defineEmits<{
  'update:activeTab': [value: 'build' | 'code' | 'backend-server' | 'ai' | 'logs' | 'settings']
}>()

const tabs = [
  { key: 'build', label: 'Editor', icon: 'pi pi-eye' },
  { key: 'code', label: 'Code', icon: 'pi pi-file-edit' },
  { key: 'backend-server', label: 'Backend', icon: 'pi pi-server' },
  { key: 'ai', label: 'AI', icon: 'pi pi-sparkles' },
  { key: 'logs', label: 'Logs', icon: 'pi pi-list' },
  { key: 'settings', label: 'Settings', icon: 'pi pi-cog' },
] as const
</script>

<template>
  <div class="editor-tabs-bar">
    <div class="view-tabs">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        class="view-tab"
        :class="{ active: activeTab === tab.key }"
        @click="emit('update:activeTab', tab.key)"
      >
        <i :class="tab.icon"></i>
        {{ tab.label }}
      </button>
    </div>

    <div class="tabs-bar-status" v-if="editorModeNoticeLabel || deployAppStatusMessage || deployStatusMessage">
      <span
        v-if="editorModeNoticeLabel"
        v-tooltip.bottom="editorModeNoticeText || ''"
        class="editor-mode-chip"
        :class="{
          'editor-mode-chip-live': showsLocalLiveBanner,
          'editor-mode-chip-source': showsSourceDeployBanner && !showsLocalLiveBanner,
        }"
      >
        <i :class="showsLocalLiveBanner ? 'pi pi-bolt' : 'pi pi-code'"></i>
        {{ editorModeNoticeLabel }}
      </span>
      <span v-if="deployAppStatusMessage" class="status-text text-sm text-color-secondary">
        {{ deployAppStatusMessage }}
      </span>
      <span v-if="deployStatusMessage" class="status-text text-sm text-color-secondary">
        {{ deployStatusMessage }}
      </span>
    </div>
  </div>
</template>

<style scoped>
.editor-tabs-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0 1rem 0.5rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.view-tabs {
  display: flex;
  gap: 0.25rem;
  min-width: 0;
  overflow-x: auto;
}

.view-tab {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.5rem 0.875rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  font-weight: 600;
  cursor: pointer;
  border-radius: var(--p-border-radius);
  transition: all 0.15s ease;
  white-space: nowrap;
}

.view-tab:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.view-tab.active {
  background: var(--p-primary-100);
  color: var(--p-primary-600);
}

.view-tab i {
  font-size: 0.75rem;
}

.tabs-bar-status {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
  min-width: 0;
  flex-wrap: wrap;
}

.status-text {
  white-space: nowrap;
}

.editor-mode-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  max-width: 14rem;
  padding: 0.375rem 0.625rem;
  border-radius: 999px;
  border: 1px solid var(--p-surface-300);
  background: var(--p-surface-0);
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 1;
}

.editor-mode-chip i {
  font-size: 0.75rem;
}

.editor-mode-chip-live {
  border-color: color-mix(in srgb, var(--p-orange-500) 35%, var(--p-surface-300));
  background: color-mix(in srgb, var(--p-orange-50) 70%, var(--p-surface-0));
  color: var(--p-orange-700);
}

.editor-mode-chip-source {
  border-color: color-mix(in srgb, var(--p-primary-500) 35%, var(--p-surface-300));
  background: color-mix(in srgb, var(--p-primary-50) 70%, var(--p-surface-0));
  color: var(--p-primary-700);
}

:global(.app-dark) .editor-tabs-bar {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .view-tab:hover {
  background: var(--p-surface-700);
}

:global(.app-dark) .view-tab.active {
  background: var(--p-primary-900);
  color: var(--p-primary-300);
}

@media (max-width: 960px) {
  .editor-tabs-bar {
    align-items: flex-start;
    flex-direction: column;
    padding: 0 0.75rem 0.5rem;
  }

  .tabs-bar-status {
    justify-content: flex-start;
  }
}
</style>
