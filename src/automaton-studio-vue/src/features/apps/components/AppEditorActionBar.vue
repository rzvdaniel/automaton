<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'
import type { AppChatStats } from '@/shared/types/ai.types'

const props = defineProps<{
  activeTab: 'build' | 'files' | 'services' | 'logs' | 'settings'
  hasRunners: boolean | null
  selectedRunnerId: string | null
  isSaving: boolean
  hasUnsavedChanges: boolean
  chatStats: AppChatStats | null
  canDeployFromStudio: boolean
  canDeployFrontendFromStudio: boolean
  isDeployingApp: boolean
  isDeployingBackend: boolean
  isPublished: boolean
  hasDeployedFrontend: boolean
  publicUrl: string | null | undefined
  appId: string | null | undefined
}>()

const emit = defineEmits<{
  'update:selectedRunnerId': [value: string | null]
  runnersLoaded: [count: number]
  saveAll: []
  preview: []
  openApp: []
  deployApp: []
  deployFrontend: []
  deployBackend: []
  publish: []
  openPublishedApp: []
  openPublishingSettings: []
  unpublish: []
}>()

const actionMenu = ref<InstanceType<typeof Menu> | null>(null)

const isEditorActionTab = computed(
  () => props.activeTab === 'build' || props.activeTab === 'files' || props.activeTab === 'services',
)

const openAppLabel = computed(() => {
  if (props.isPublished) {
    return 'Open Public App'
  }

  if (props.hasDeployedFrontend) {
    return 'Open Private App'
  }

  return 'Open App'
})

const actionMenuItems = computed<MenuItem[]>(() => {
  const items: MenuItem[] = []

  if (props.isPublished || props.hasDeployedFrontend) {
    items.push({
      label: openAppLabel.value,
      icon: 'pi pi-external-link',
      command: () => emit('openApp'),
    })
    items.push({ separator: true })
  }

  if (props.isPublished) {
    items.push({
      label: 'Open Published App',
      icon: 'pi pi-external-link',
      command: () => emit('openPublishedApp'),
    })
    items.push({
      label: 'Publishing Settings',
      icon: 'pi pi-cog',
      command: () => emit('openPublishingSettings'),
    })
    items.push({ separator: true })
  } else {
    items.push({
      label: 'Publish App',
      icon: 'pi pi-cloud-upload',
      command: () => emit('publish'),
    })
    items.push({ separator: true })
  }

  items.push({
    label: 'Deploy App',
    icon: 'pi pi-cloud-upload',
    disabled: props.isDeployingApp || !props.canDeployFromStudio,
    command: () => emit('deployApp'),
  })
  items.push({
    label: 'Deploy Frontend',
    icon: 'pi pi-desktop',
    disabled: props.isDeployingApp || !props.canDeployFrontendFromStudio,
    command: () => emit('deployFrontend'),
  })
  items.push({
    label: 'Deploy Backend',
    icon: 'pi pi-server',
    disabled: props.isDeployingBackend,
    command: () => emit('deployBackend'),
  })

  if (props.isPublished) {
    items.push({ separator: true })
    items.push({
      label: 'Unpublish',
      icon: 'pi pi-cloud-download',
      command: () => emit('unpublish'),
    })
  }

  return items
})

const primaryActionLabel = computed(() => {
  if (props.isPublished || props.hasDeployedFrontend) {
    return openAppLabel.value
  }

  return props.canDeployFromStudio ? 'Publish' : 'Deploy'
})

const primaryActionIcon = computed(() => {
  if (props.isPublished || props.hasDeployedFrontend) {
    return 'pi pi-external-link'
  }

  if (props.canDeployFromStudio) {
    return 'pi pi-cloud-upload'
  }

  return 'pi pi-cloud'
})

const isPrimaryActionDisabled = computed(() => {
  if (props.isPublished || props.hasDeployedFrontend) {
    return false
  }

  return !props.canDeployFromStudio || props.isDeployingApp
})

const isPrimaryActionLoading = computed(() => {
  if (props.isPublished || props.hasDeployedFrontend) {
    return false
  }

  return props.canDeployFromStudio && props.isDeployingApp
})

function toggleActionMenu(event: Event) {
  actionMenu.value?.toggle(event)
}

function handlePrimaryAction() {
  if (props.isPublished || props.hasDeployedFrontend) {
    emit('openApp')
    return
  }

  if (props.canDeployFromStudio) {
    emit('publish')
  }
}

function formatTokenCount(count: number): string {
  if (count >= 1000000) return `${(count / 1000000).toFixed(1)}M`
  if (count >= 1000) return `${(count / 1000).toFixed(1)}K`
  return count.toString()
}
</script>

<template>
  <div v-if="isEditorActionTab" class="editor-action-bar">
    <div class="action-bar-left">
      <div v-if="hasRunners !== false" class="runtime-chip">
        <span class="runtime-label">
          <i class="pi pi-circle-fill runtime-dot"></i>
          Runtime
        </span>
        <RunnerSelector
          :model-value="selectedRunnerId"
          :disabled="isSaving"
          @update:model-value="emit('update:selectedRunnerId', $event)"
          @runners-loaded="emit('runnersLoaded', $event)"
        />
      </div>

      <Button
        v-if="activeTab !== 'services'"
        v-tooltip.bottom="'Open full-screen preview'"
        icon="pi pi-eye"
        label="Preview"
        outlined
        severity="secondary"
        @click="emit('preview')"
      />

      <div v-if="activeTab !== 'services'" class="deploy-actions">
        <Button
          :label="primaryActionLabel"
          :icon="primaryActionIcon"
          :severity="isPublished ? 'primary' : 'success'"
          :disabled="isPrimaryActionDisabled"
          :loading="isPrimaryActionLoading"
          @click="handlePrimaryAction"
        />
        <Button
          icon="pi pi-chevron-down"
          :severity="isPublished ? 'primary' : 'success'"
          aria-label="More deploy actions"
          @click="toggleActionMenu"
        />
        <Menu ref="actionMenu" :model="actionMenuItems" :popup="true" />
      </div>
    </div>

    <div class="action-bar-right">
      <Button
        v-if="hasUnsavedChanges && activeTab === 'files'"
        label="Save All"
        icon="pi pi-save"
        :loading="isSaving"
        outlined
        severity="secondary"
        @click="emit('saveAll')"
      />

      <div v-if="chatStats" class="chat-stats">
        <Button
          v-tooltip.bottom="`${chatStats.totalPromptsSubmitted} prompts submitted`"
          :label="`${chatStats.totalPromptsSubmitted}`"
          icon="pi pi-comments"
          text
          severity="secondary"
          size="small"
        />
        <Button
          v-tooltip.bottom="`${chatStats.totalTokens} tokens used (${chatStats.inputTokens} in / ${chatStats.outputTokens} out)`"
          :label="formatTokenCount(chatStats.totalTokens)"
          icon="pi pi-zap"
          text
          severity="secondary"
          size="small"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.editor-action-bar {
  position: sticky;
  top: 0;
  z-index: 5;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  background: color-mix(in srgb, var(--p-surface-0) 92%, transparent);
  backdrop-filter: blur(8px);
  flex-shrink: 0;
}

.action-bar-left,
.action-bar-right {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  min-width: 0;
}

.runtime-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.375rem 0.5rem;
  border: 1px solid var(--p-surface-300);
  border-radius: 999px;
  background: var(--p-surface-0);
}

.runtime-label {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.runtime-dot {
  font-size: 0.5rem;
  color: var(--p-green-500);
}

.deploy-actions {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
}

.chat-stats {
  display: inline-flex;
  align-items: center;
  gap: 0.125rem;
}

:global(.app-dark) .editor-action-bar {
  border-color: var(--p-surface-700);
  background: color-mix(in srgb, var(--p-surface-900) 90%, transparent);
}

:global(.app-dark) .runtime-chip {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
}

@media (max-width: 1100px) {
  .editor-action-bar {
    align-items: flex-start;
    flex-direction: column;
    padding: 0.75rem;
  }

  .action-bar-left,
  .action-bar-right {
    width: 100%;
    flex-wrap: wrap;
  }
}
</style>
