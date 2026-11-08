import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { appsService } from '../services/apps.service'
import { getAppOperationalSummary, isActionOnlyApp } from '../utils/app-operational-summary'
import type {
  App,
  AppDeploymentTarget,
  ApplicationViewportMode,
} from '../types/apps.types'
import type { BackendRuntimeLogEvent, ConsoleEntry } from '../types/console.types'

export const useAppEditorStore = defineStore('appEditor', () => {
  const currentApp = ref<App | null>(null)
  const defaultTarget = ref<AppDeploymentTarget | null>(null)
  const applicationViewportMode = ref<ApplicationViewportMode>('desktop')
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const consoleEntries = ref<ConsoleEntry[]>([])

  const isPublished = computed(() => currentApp.value?.isPublished ?? false)
  const applicationState = computed(() => getAppOperationalSummary(defaultTarget.value, isActionOnlyApp(currentApp.value)))

  async function loadApp(appId: string): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      currentApp.value = await appsService.get(appId)
    } catch (caught: unknown) {
      error.value = caught instanceof Error ? caught.message : 'Failed to load app'
      throw caught
    } finally {
      isLoading.value = false
    }
  }

  function setDefaultTarget(target: AppDeploymentTarget | null): void {
    defaultTarget.value = target
  }

  /** Reloads the app in place, without the page-wide loading state, so open dialogs stay open. */
  async function refreshApp(appId: string): Promise<void> {
    currentApp.value = await appsService.get(appId)
  }

  function addConsoleEntry(entry: ConsoleEntry): void {
    consoleEntries.value = [entry, ...consoleEntries.value].slice(0, 100)
  }

  function addBackendRuntimeLogEntry(event: BackendRuntimeLogEvent): void {
    addConsoleEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(event.timestamp),
      type: 'runtime-log',
      stepName: event.actionName || event.controllerName || 'backend',
      inputs: {},
      result: {
        success: event.level !== 'error',
        status: event.level === 'error' ? 'Error' : event.level === 'warn' ? 'Warning' : 'Output',
        output: event.message,
      },
      source: 'application',
      level: event.level,
      stream: event.stream,
      runnerName: event.runnerName ?? undefined,
      deploymentTargetId: event.deploymentTargetId,
    })
  }

  function clearConsole(): void {
    consoleEntries.value = []
  }

  function reset(): void {
    currentApp.value = null
    defaultTarget.value = null
    applicationViewportMode.value = 'desktop'
    isLoading.value = false
    error.value = null
    consoleEntries.value = []
  }

  return {
    currentApp,
    applicationViewportMode,
    isLoading,
    error,
    consoleEntries,
    isPublished,
    applicationState,
    loadApp,
    setDefaultTarget,
    refreshApp,
    addConsoleEntry,
    addBackendRuntimeLogEntry,
    clearConsole,
    reset,
  }
})
