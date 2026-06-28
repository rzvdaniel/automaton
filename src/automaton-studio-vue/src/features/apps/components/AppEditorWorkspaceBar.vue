<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
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
const isStatusDetailsOpen = ref(false)
let statusAutoCollapseTimer: number | null = null

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
  if (props.isDeployingApp) {
    return { icon: 'pi pi-spin pi-spinner', severity: 'info' as const, shortLabel: 'Deploying' }
  }

  switch (props.applicationState) {
    case 'Deployed':
      return { icon: 'pi pi-check-circle', severity: 'success' as const, shortLabel: 'Everything deployed' }
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

const statusDotClass = computed(() => `status-dot status-dot--${statusUi.value.severity}`)
const statusSummary = computed(() => {
  if (props.isDeployingApp) {
    return { icon: 'pi pi-spin pi-spinner', severity: 'info' as const, label: 'Deploying' }
  }

  if (props.applicationState === 'Deployed') {
    return { icon: 'pi pi-check-circle', severity: 'success' as const, label: 'Healthy' }
  }

  if (props.applicationState === 'Source changed') {
    return { icon: 'pi pi-exclamation-triangle', severity: 'warn' as const, label: 'Ready to deploy' }
  }

  if (props.applicationState === 'Build failed') {
    return { icon: 'pi pi-times-circle', severity: 'danger' as const, label: 'Build failed' }
  }

  if (props.applicationState === 'Runner offline') {
    return { icon: 'pi pi-ban', severity: 'contrast' as const, label: 'Runner offline' }
  }

  return { icon: statusUi.value.icon, severity: statusUi.value.severity, label: 'Not deployed' }
})

const runnerStatus = computed(() => {
  if (props.applicationState === 'Runner offline') {
    return { icon: 'pi pi-times-circle', severity: 'danger' as const, label: 'Runner offline' }
  }

  return { icon: 'pi pi-check-circle', severity: 'success' as const, label: 'Runner connected' }
})

const deploymentStatus = computed(() => {
  if (props.isDeployingApp) {
    return { icon: 'pi pi-spin pi-spinner', severity: 'info' as const, label: 'Deployment in progress' }
  }

  if (props.applicationState === 'Source changed') {
    return { icon: 'pi pi-exclamation-triangle', severity: 'warn' as const, label: 'Source changes pending deployment' }
  }

  if (props.applicationState === 'Build failed') {
    return { icon: 'pi pi-times-circle', severity: 'danger' as const, label: 'Last deploy failed' }
  }

  if (props.applicationState === 'Not deployed') {
    return { icon: 'pi pi-info-circle', severity: 'secondary' as const, label: 'Application not deployed' }
  }

  return { icon: 'pi pi-check-circle', severity: 'success' as const, label: 'No pending changes' }
})

const publishingStatus = computed(() => {
  if (props.isPublished) {
    return { icon: 'pi pi-check-circle', severity: 'success' as const, label: 'Published' }
  }

  return { icon: 'pi pi-check-circle', severity: 'success' as const, label: 'Unpublished' }
})

const statusIssuesCount = computed(() => {
  let count = 0

  if (runnerStatus.value.severity !== 'success') {
    count += 1
  }

  if (deploymentStatus.value.severity !== 'success') {
    count += 1
  }

  if (publishingStatus.value.severity !== 'success') {
    count += 1
  }

  return count
})

const statusIssuesLabel = computed(() => {
  if (statusIssuesCount.value === 0) {
    return 'No issues'
  }

  if (statusIssuesCount.value === 1) {
    return '1 issue requires attention'
  }

  return `${statusIssuesCount.value} issues require attention`
})

const shouldAutoExpandStatusDetails = computed(() => {
  return props.applicationState === 'Build failed' || props.applicationState === 'Runner offline'
})

const shouldAutoCollapseStatusDetails = computed(() => {
  return statusSummary.value.label === 'Healthy'
})

function clearStatusAutoCollapseTimer() {
  if (statusAutoCollapseTimer != null) {
    window.clearTimeout(statusAutoCollapseTimer)
    statusAutoCollapseTimer = null
  }
}

function scheduleStatusAutoCollapse() {
  clearStatusAutoCollapseTimer()

  if (!shouldAutoCollapseStatusDetails.value || !isStatusDetailsOpen.value) {
    return
  }

  statusAutoCollapseTimer = window.setTimeout(() => {
    if (shouldAutoCollapseStatusDetails.value) {
      isStatusDetailsOpen.value = false
    }
    statusAutoCollapseTimer = null
  }, 4000)
}

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
  isStatusDetailsOpen.value = shouldAutoExpandStatusDetails.value
  statusPopover.value?.toggle(event)
  scheduleStatusAutoCollapse()
}

function toggleStatusDetails() {
  isStatusDetailsOpen.value = !isStatusDetailsOpen.value
  scheduleStatusAutoCollapse()
}

function handlePrimaryAction() {
  emit('deployApp')
}

watch(
  () => statusSummary.value.label,
  () => {
    scheduleStatusAutoCollapse()
  },
)

watch(isStatusDetailsOpen, (isOpen) => {
  if (isOpen) {
    scheduleStatusAutoCollapse()
    return
  }

  clearStatusAutoCollapseTimer()
})

onBeforeUnmount(() => {
  clearStatusAutoCollapseTimer()
})
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
        <Button
          v-if="showsEditorActions"
          text
          size="small"
          class="status-indicator-button"
          rounded
          :aria-label="`Application status: ${statusUi.shortLabel}`"
          v-tooltip.bottom="`Application status: ${statusUi.shortLabel}`"
          @click="toggleStatusPopover"
        >
          <span :class="statusDotClass" aria-hidden="true"></span>
        </Button>
        <Popover ref="statusPopover" class="status-popover">
          <div class="status-popover__content">
            <div class="status-popover__title-row">
              <div class="status-popover__eyebrow">Application Status</div>
              <div class="status-popover__header">
                <i :class="[statusSummary.icon, `status-popover__icon status-popover__icon--${statusSummary.severity}`]"></i>
                <div>
                  <div class="status-popover__title">{{ statusSummary.label }}</div>
                </div>
              </div>
            </div>
            <div class="status-popover__summary-line">
              {{ statusIssuesLabel }}
            </div>
            <button
              type="button"
              class="status-popover__toggle"
              :aria-expanded="isStatusDetailsOpen"
              @click="toggleStatusDetails"
            >
              <span>{{ isStatusDetailsOpen ? 'Hide details' : 'Show details' }}</span>
              <i :class="['pi', isStatusDetailsOpen ? 'pi-chevron-up' : 'pi-chevron-down']"></i>
            </button>
            <div v-if="isStatusDetailsOpen" class="status-popover__details">
              <div class="status-popover__section">
                <div class="status-popover__section-label">Runtime</div>
                <div class="status-popover__item">
                  <i :class="[runnerStatus.icon, `status-popover__icon status-popover__icon--${runnerStatus.severity}`]"></i>
                  <span>{{ runnerStatus.label }}</span>
                </div>
              </div>
              <div class="status-popover__section">
                <div class="status-popover__section-label">Deployment</div>
                <div class="status-popover__item">
                  <i :class="[deploymentStatus.icon, `status-popover__icon status-popover__icon--${deploymentStatus.severity}`]"></i>
                  <span>{{ deploymentStatus.label }}</span>
                </div>
              </div>
              <div class="status-popover__section">
                <div class="status-popover__section-label">Publishing</div>
                <div class="status-popover__item">
                  <i :class="[publishingStatus.icon, `status-popover__icon status-popover__icon--${publishingStatus.severity}`]"></i>
                  <span>{{ publishingStatus.label }}</span>
                </div>
              </div>
            </div>
            <Button
              v-if="props.applicationState !== 'Deployed'"
              label="Deploy"
              icon="pi pi-cloud-upload"
              size="small"
              :loading="props.isDeployingApp"
              :disabled="!props.canDeployFromStudio || props.isDeployingApp"
              @click="handlePrimaryAction"
            />
          </div>
        </Popover>
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
  min-width: 2.2rem;
  width: 2.2rem;
  height: 2.2rem;
  padding: 0;
}

.status-indicator-button :deep(.p-button-label) {
  display: none;
}

.status-indicator-button :deep(.p-button-icon) {
  display: none;
}

.status-dot {
  width: 0.72rem;
  height: 0.72rem;
  border-radius: 999px;
  background: var(--p-text-muted-color);
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--p-surface-0) 55%, transparent);
}

.status-dot--success {
  background: var(--p-green-500);
}

.status-dot--warn {
  background: var(--p-orange-500);
}

.status-dot--danger {
  background: var(--p-red-500);
}

.status-dot--info {
  background: var(--p-blue-500);
  animation: status-dot-pulse 1.2s ease-in-out infinite;
}

.status-dot--contrast,
.status-dot--secondary {
  background: var(--p-text-muted-color);
}

.status-popover__content {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
  width: min(22rem, 70vw);
}

.status-popover__title-row {
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
}

.status-popover__eyebrow {
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--p-text-muted-color);
}

.status-popover__header {
  display: flex;
  align-items: flex-start;
  gap: 0.7rem;
}

.status-popover__title {
  font-weight: 700;
}

.status-popover__summary-line {
  color: var(--p-text-muted-color);
  font-size: 0.88rem;
}

.status-popover__toggle {
  display: inline-flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  width: 100%;
  border: none;
  background: transparent;
  padding: 0;
  color: var(--p-primary-600);
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  text-align: left;
}

.status-popover__toggle:hover {
  color: var(--p-primary-500);
}

.status-popover__details {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}
.status-popover__section {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.status-popover__section-label {
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--p-text-muted-color);
}

.status-popover__item {
  display: flex;
  align-items: center;
  gap: 0.55rem;
  font-size: 0.9rem;
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

.status-popover__icon--info {
  color: var(--p-blue-500);
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

:global(.app-dark) .status-dot {
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--p-surface-900) 72%, transparent);
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

@keyframes status-dot-pulse {
  0%,
  100% {
    opacity: 1;
    transform: scale(1);
  }

  50% {
    opacity: 0.55;
    transform: scale(0.9);
  }
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
