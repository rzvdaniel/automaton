<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Menu from 'primevue/menu'
import Popover from 'primevue/popover'
import Tag from 'primevue/tag'
import type { MenuItem } from 'primevue/menuitem'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'
import { useUserSettings } from '@/shared/composables/useUserSettings'

const { isDark } = useUserSettings()

const props = defineProps<{
  activeTab: 'build' | 'files' | 'versions' | 'services' | 'logs' | 'settings'
  appName: string
  isEditingName: boolean
  editingName: string
  isPublished: boolean
  hasRunners: boolean | null
  selectedRunnerId: string | null
  isSaving: boolean
  hasUnsavedChanges: boolean
  canDeployFromStudio: boolean
  canDeployFrontendFromStudio: boolean
  isDeployingApp: boolean
  isDeployingBackend: boolean
  applicationState: 'Deployed' | 'Source changed' | 'Build failed' | 'Runner offline' | 'Not deployed'
  applicationStatusMessage: string | null
  applicationRuntimeTimestampText: string | null
  applicationSourceTimestampText: string | null
  sourceChangedSinceDeploy: boolean
}>()

const emit = defineEmits<{
  back: []
  'update:editingName': [value: string]
  startEditing: []
  saveName: []
  cancelEditing: []
  'update:selectedRunnerId': [value: string | null]
  runnersLoaded: [count: number]
  saveAll: []
  deployApp: []
  deployFrontend: []
  deployBackend: []
  publish: []
  openPublishingSettings: []
  unpublish: []
}>()

const actionMenu = ref<InstanceType<typeof Menu> | null>(null)
const publishedMenu = ref<InstanceType<typeof Menu> | null>(null)
const statusPopover = ref<InstanceType<typeof Popover> | null>(null)

const showsEditorActions = computed(() =>
  props.activeTab === 'build' || props.activeTab === 'files' || props.activeTab === 'services',
)

const actionMenuItems = computed<MenuItem[]>(() => {
  const items: MenuItem[] = []

  items.push({
    label: 'Deploy Frontend Only',
    disabled: props.isDeployingApp || !props.canDeployFrontendFromStudio,
    command: () => emit('deployFrontend'),
  })
  items.push({
    label: 'Deploy Backend Only',
    disabled: props.isDeployingBackend,
    command: () => emit('deployBackend'),
  })

  return items
})

const isPrimaryActionDisabled = computed(() => {
  return !props.canDeployFromStudio || props.isDeployingApp
})

const statusUi = computed(() => {
  switch (props.applicationState) {
    case 'Deployed':
      return { icon: props.isDeployingApp ? 'pi pi-spin pi-spinner' : 'pi pi-check-circle', severity: 'success' as const, shortLabel: '' }
    case 'Source changed':
      return { icon: 'pi pi-exclamation-triangle', severity: 'warn' as const, shortLabel: 'Source changed' }
    case 'Build failed':
      return { icon: 'pi pi-times-circle', severity: 'danger' as const, shortLabel: 'Build failed' }
    case 'Runner offline':
      return { icon: 'pi pi-ban', severity: 'contrast' as const, shortLabel: 'Runner offline' }
    default:
      return { icon: 'pi pi-info-circle', severity: 'secondary' as const, shortLabel: 'Not deployed' }
  }
})

const showStatusText = computed(() => props.applicationState !== 'Deployed')

function getGroupedMenuEvent(event: Event, selector: string) {
  const trigger = event.currentTarget instanceof HTMLElement
    ? event.currentTarget.closest(selector) ?? event.currentTarget
    : event.currentTarget

  return {
    ...event,
    currentTarget: trigger,
    target: trigger,
  }
}

function toggleActionMenu(event: Event) {
  actionMenu.value?.toggle(getGroupedMenuEvent(event, '.deploy-actions'))
}

function togglePublishedMenu(event: Event) {
  publishedMenu.value?.toggle(getGroupedMenuEvent(event, '.published-actions'))
}

function toggleStatusPopover(event: Event) {
  statusPopover.value?.toggle(event)
}

function handlePrimaryAction() {
  emit('deployApp')
}
</script>

<template>
  <div class="workspace-bar" :class="{ 'workspace-bar-dark': isDark }">
    <div class="workspace-main">
      <div class="workspace-identity">
        <Button
          v-tooltip.bottom="'Back to Apps'"
          icon="pi pi-arrow-left"
          text
          rounded
          severity="secondary"
          aria-label="Back to Apps"
          @click="emit('back')"
        />
        <span v-if="!isEditingName" class="app-name" @click="emit('startEditing')">
          {{ appName }}
        </span>
        <InputText
          v-else
          :model-value="editingName"
          class="app-name-input"
          @update:model-value="emit('update:editingName', $event ?? '')"
          @blur="emit('saveName')"
          @keyup.enter="emit('saveName')"
          @keyup.escape="emit('cancelEditing')"
        />
        <Tag v-if="isPublished" value="Published" severity="success" />
        <Tag v-else value="Draft" severity="warn" />
      </div>

    </div>

    <div class="workspace-actions">
      <div v-if="showsEditorActions && hasRunners !== false" class="runtime-control">
        <RunnerSelector
          :model-value="selectedRunnerId"
          :disabled="isSaving"
          @update:model-value="emit('update:selectedRunnerId', $event)"
          @runners-loaded="emit('runnersLoaded', $event)"
        />
      </div>

      <Button
        v-if="showsEditorActions"
        :icon="statusUi.icon"
        :label="showStatusText ? statusUi.shortLabel : undefined"
        :severity="statusUi.severity"
        text
        size="small"
        class="status-indicator-button"
        @click="toggleStatusPopover"
      />
      <Popover ref="statusPopover" class="status-popover">
        <div class="status-popover__content">
          <div class="status-popover__header">
            <i :class="[statusUi.icon, `status-popover__icon status-popover__icon--${statusUi.severity}`]"></i>
            <div>
              <div class="status-popover__title">{{ props.applicationState }}</div>
              <div v-if="props.applicationStatusMessage" class="status-popover__message">
                {{ props.applicationStatusMessage }}
              </div>
            </div>
          </div>
          <div v-if="props.applicationRuntimeTimestampText" class="status-popover__meta">
            {{ props.applicationRuntimeTimestampText }}
          </div>
          <div v-if="props.sourceChangedSinceDeploy && props.applicationSourceTimestampText" class="status-popover__meta">
            {{ props.applicationSourceTimestampText }}
          </div>
          <Button
            v-if="props.applicationState !== 'Deployed'"
            label="Deploy now"
            icon="pi pi-cloud-upload"
            size="small"
            :loading="props.isDeployingApp"
            :disabled="!props.canDeployFromStudio || props.isDeployingApp"
            @click="handlePrimaryAction"
          />
        </div>
      </Popover>

      <div v-if="showsEditorActions && activeTab !== 'services'" class="deploy-actions">
        <Button
          label="Deploy"
          icon="pi pi-cloud-upload"
          class="deploy-main-button"
          severity="success"
          :disabled="isPrimaryActionDisabled"
          :loading="isDeployingApp"
          @click="handlePrimaryAction"
        />
        <Button
          icon="pi pi-chevron-down"
          class="deploy-menu-button"
          severity="success"
          aria-label="More deploy actions"
          @click="toggleActionMenu"
        />
        <Menu ref="actionMenu" class="deploy-menu" :model="actionMenuItems" :popup="true" />
      </div>

      <Button
        v-if="showsEditorActions && !isPublished"
        label="Publish"
        icon="pi pi-globe"
        outlined
        severity="success"
        @click="emit('publish')"
      />

      <div v-else-if="showsEditorActions" class="published-actions">
        <Button
          label="Published"
          icon="pi pi-globe"
          outlined
          severity="secondary"
          class="published-main-button"
          @click="emit('openPublishingSettings')"
        />
        <Button
          icon="pi pi-chevron-down"
          outlined
          severity="secondary"
          class="published-menu-button"
          aria-label="Published actions"
          @click="togglePublishedMenu"
        />
        <Menu
          ref="publishedMenu"
          class="deploy-menu"
          :model="[
            { label: 'Publishing Settings', command: () => emit('openPublishingSettings') },
            { label: 'Unpublish', command: () => emit('unpublish') },
          ]"
          :popup="true"
        />
      </div>

      <Button
        v-if="hasUnsavedChanges && activeTab === 'files'"
        label="Save All"
        icon="pi pi-save"
        :loading="isSaving"
        outlined
        severity="secondary"
        @click="emit('saveAll')"
      />

    </div>
  </div>
</template>

<style scoped>
.workspace-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.625rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  background: color-mix(in srgb, var(--p-surface-0) 95%, transparent);
  backdrop-filter: blur(10px);
  flex-shrink: 0;
}

.workspace-bar-dark {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
}

.workspace-main {
  display: flex;
  align-items: center;
  gap: 1rem;
  min-width: 0;
  flex: 1;
}

.workspace-identity {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
  flex-shrink: 0;
}

.app-name {
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  color: var(--p-text-color);
  max-width: 14rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-name:hover {
  text-decoration: underline;
}

.app-name-input {
  width: min(16rem, 100%);
  font-size: 1rem;
  font-weight: 600;
}

.workspace-tabs {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  min-width: 0;
  overflow-x: auto;
}

.workspace-tab {
  position: relative;
  display: inline-flex;
  align-items: center;
  padding: 0.625rem 0.75rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  transition: color 0.15s ease;
}

.workspace-tab::after {
  content: '';
  position: absolute;
  left: 0.75rem;
  right: 0.75rem;
  bottom: 0.125rem;
  height: 2px;
  border-radius: 999px;
  background: transparent;
  transition: background 0.15s ease;
}

.workspace-tab:hover {
  color: var(--p-text-color);
}

.workspace-tab.active {
  color: var(--p-text-color);
}

.workspace-tab.active::after {
  background: var(--p-primary-500);
}

.workspace-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-shrink: 0;
}

.runtime-control {
  display: inline-flex;
  align-items: center;
}

.status-indicator-button {
  gap: 0.35rem;
}

.status-popover__content {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
  width: min(22rem, 70vw);
}

.status-popover__header {
  display: flex;
  align-items: flex-start;
  gap: 0.7rem;
}

.status-popover__title {
  font-weight: 700;
}

.status-popover__message {
  margin-top: 0.2rem;
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
  line-height: 1.45;
}

.status-popover__meta {
  color: var(--p-text-muted-color);
  font-size: 0.82rem;
}

.status-popover__icon {
  margin-top: 0.1rem;
}

.status-popover__icon--success {
  color: var(--p-green-500);
}

.status-popover__icon--warn {
  color: var(--p-orange-500);
}

.status-popover__icon--danger {
  color: var(--p-red-500);
}

.status-popover__icon--contrast,
.status-popover__icon--secondary {
  color: var(--p-text-muted-color);
}

.deploy-actions {
  display: inline-flex;
  align-items: center;
  gap: 0;
  border-radius: 10px;
  box-shadow: 0 1px 2px rgba(16, 24, 40, 0.04);
  overflow: hidden;
}

.published-actions {
  display: inline-flex;
  align-items: center;
  gap: 0;
  border-radius: 10px;
  box-shadow: 0 1px 2px rgba(16, 24, 40, 0.04);
  overflow: hidden;
}

.deploy-actions :deep(.deploy-main-button) {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
}

.deploy-actions :deep(.deploy-menu-button) {
  min-width: 2.5rem;
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
  border-left: 1px solid color-mix(in srgb, white 26%, transparent);
}

.published-actions :deep(.published-main-button) {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
  border-right: 0;
}

.published-actions :deep(.published-menu-button) {
  min-width: 2.5rem;
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
  border-left: 1px solid color-mix(in srgb, var(--p-surface-300) 85%, transparent);
}

.published-actions :deep(.published-menu-button .p-button-icon) {
  font-size: 0.8rem;
}

.deploy-actions :deep(.deploy-menu-button .p-button-icon) {
  font-size: 0.8rem;
}

:global(.app-dark) .deploy-actions {
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.22);
}

:global(.app-dark) .deploy-actions .deploy-menu-button {
  border-left-color: color-mix(in srgb, white 14%, transparent);
}

:global(.app-dark) .published-actions {
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.22);
}

:global(.app-dark) .published-actions .published-menu-button {
  border-left-color: color-mix(in srgb, var(--p-surface-600) 75%, transparent);
}

:global(.deploy-menu.p-menu) {
  min-width: 10rem;
  border: 1px solid color-mix(in srgb, var(--p-surface-300) 75%, transparent);
  border-radius: 10px;
  box-shadow: 0 10px 28px rgba(0, 0, 0, 0.08);
}

:global(.deploy-menu .p-menu-list) {
  padding: 0.2rem;
}

:global(.deploy-menu .p-menu-item-link) {
  padding: 0.38rem 0.55rem;
  border-radius: 7px;
  font-size: 0.8125rem;
}

:global(.deploy-menu .p-menu-separator) {
  margin: 0.12rem 0;
}

:global(.deploy-menu .p-menu-item-link:hover) {
  background: var(--p-surface-100);
}

:global(.deploy-menu .p-menu-item-link .p-menu-item-label) {
  line-height: 1.2;
}

:global(.app-dark) .deploy-menu.p-menu {
  border-color: color-mix(in srgb, var(--p-surface-700) 82%, transparent);
  box-shadow: 0 12px 30px rgba(0, 0, 0, 0.28);
}

:global(.app-dark) .deploy-menu .p-menu-item-link:hover {
  background: var(--p-surface-800);
}

@media (max-width: 1200px) {
  .workspace-bar {
    align-items: flex-start;
    flex-direction: column;
    padding: 0.75rem;
  }

  .workspace-main,
  .workspace-actions {
    width: 100%;
    flex-wrap: wrap;
  }
}
</style>
