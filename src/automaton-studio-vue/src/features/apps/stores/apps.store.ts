/**
 * Apps Pinia store
 * Manages apps data, loading states, and CRUD operations
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { appsService } from '../services/apps.service'
import type { App, AppListItem, AppListFilters, CreateAppRequest, PublishAppRequest } from '../types/apps.types'

export const useAppsStore = defineStore('apps', () => {
  // State
  const apps = ref<App[]>([])
  const listApps = ref<AppListItem[]>([])
  const selectedApp = ref<App | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const filters = ref<AppListFilters>({
    search: '',
    status: 'all',
    projectId: undefined,
  })

  // Computed
  const filteredApps = computed(() => {
    let result = listApps.value

    // Filter by status
    if (filters.value.status === 'published') {
      result = result.filter((app) => app.isPublished)
    } else if (filters.value.status === 'draft') {
      result = result.filter((app) => !app.isPublished)
    }

    // Filter by search
    if (filters.value.search) {
      const searchLower = filters.value.search.toLowerCase()
      result = result.filter(
        (app) =>
          app.name.toLowerCase().includes(searchLower) ||
          (app.description && app.description.toLowerCase().includes(searchLower)),
      )
    }

    return result
  })

  const publishedApps = computed(() => listApps.value.filter((app) => app.isPublished))

  const draftApps = computed(() => listApps.value.filter((app) => !app.isPublished))

  const appCount = computed(() => listApps.value.length)

  const publishedCount = computed(() => publishedApps.value.length)

  const draftCount = computed(() => draftApps.value.length)

  const hasApps = computed(() => listApps.value.length > 0)

  // Actions
  async function fetchApps(projectId?: string): Promise<void> {
    isLoading.value = true
    error.value = null

    try {
      listApps.value = await appsService.getApps(projectId)
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      error.value = errorMessage
      throw new Error(errorMessage)
    } finally {
      isLoading.value = false
    }
  }

  async function fetchApp(id: string): Promise<App> {
    isLoading.value = true
    error.value = null

    try {
      const app = await appsService.get(id)
      selectedApp.value = app
      return app
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      error.value = errorMessage
      throw new Error(errorMessage)
    } finally {
      isLoading.value = false
    }
  }

  async function createApp(request: CreateAppRequest): Promise<App> {
    try {
      const newApp = await appsService.create(request)
      apps.value.push(newApp)
      return newApp
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  async function updateApp(id: string, updates: Partial<App>): Promise<void> {
    try {
      await appsService.update(id, updates)

      // Update local state
      const index = apps.value.findIndex((app) => app.id === id)
      if (index !== -1) {
        apps.value[index] = { ...apps.value[index]!, ...updates }
      }
      if (selectedApp.value?.id === id) {
        selectedApp.value = { ...selectedApp.value, ...updates }
      }
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  async function deleteApp(id: string): Promise<void> {
    try {
      await appsService.delete(id)

      // Remove from local state
      apps.value = apps.value.filter((app) => app.id !== id)
      if (selectedApp.value?.id === id) {
        selectedApp.value = null
      }
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  async function publishApp(id: string, request: PublishAppRequest): Promise<void> {
    try {
      await appsService.publish(id, request)

      // Update local state
      const index = apps.value.findIndex((app) => app.id === id)
      if (index !== -1) {
        apps.value[index] = {
          ...apps.value[index]!,
          isPublished: true,
          publicUrl: request.publicUrl,
          requiresAuthentication: request.requiresAuthentication,
          publishedAt: new Date().toISOString(),
          category: request.category ?? apps.value[index]!.category,
          tags: request.tags ?? apps.value[index]!.tags,
        }
      }
      if (selectedApp.value?.id === id) {
        selectedApp.value = {
          ...selectedApp.value,
          isPublished: true,
          publicUrl: request.publicUrl,
          requiresAuthentication: request.requiresAuthentication,
          publishedAt: new Date().toISOString(),
          category: request.category ?? selectedApp.value.category,
          tags: request.tags ?? selectedApp.value.tags,
        }
      }
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  async function unpublishApp(id: string): Promise<void> {
    try {
      await appsService.unpublish(id)

      // Update local state
      const index = apps.value.findIndex((app) => app.id === id)
      if (index !== -1) {
        apps.value[index] = {
          ...apps.value[index]!,
          isPublished: false,
          publishedAt: undefined,
        }
      }
      if (selectedApp.value?.id === id) {
        selectedApp.value = {
          ...selectedApp.value,
          isPublished: false,
          publishedAt: undefined,
        }
      }
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  function setSearchFilter(search: string): void {
    filters.value.search = search
  }

  function setStatusFilter(status: 'all' | 'published' | 'draft'): void {
    filters.value.status = status
  }

  function setProjectFilter(projectId: string | undefined): void {
    filters.value.projectId = projectId
  }

  function clearFilters(): void {
    filters.value = {
      search: '',
      status: 'all',
      projectId: undefined,
    }
  }

  function clearError(): void {
    error.value = null
  }

  function reset(): void {
    apps.value = []
    listApps.value = []
    selectedApp.value = null
    isLoading.value = false
    error.value = null
    clearFilters()
  }

  async function duplicateApp(id: string): Promise<App> {
    try {
      const newApp = await appsService.duplicate(id)
      apps.value.push(newApp)
      return newApp
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  async function forkTemplate(id: string): Promise<App> {
    try {
      const newApp = await appsService.fork(id)
      apps.value.push(newApp)
      return newApp
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  async function importApp(file: File): Promise<App> {
    try {
      const importedApp = await appsService.importApp(file)
      apps.value.unshift(importedApp)
      return importedApp
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      throw new Error(errorMessage)
    }
  }

  // Helper to extract error message
  function getErrorMessage(err: unknown): string {
    if (err && typeof err === 'object') {
      const axiosError = err as {
        response?: { data?: { message?: string; title?: string }; status?: number }
        message?: string
      }
      if (axiosError.response?.data?.message) {
        return axiosError.response.data.message
      }
      if (axiosError.response?.data?.title) {
        return axiosError.response.data.title
      }
      if (axiosError.response?.status === 401) {
        return 'Authentication required. Please log in again.'
      }
      if (axiosError.response?.status === 403) {
        return 'You do not have permission to perform this action.'
      }
      if (axiosError.response?.status === 404) {
        return 'App not found.'
      }
      if (axiosError.response?.status === 500) {
        return 'Server error. Please try again later.'
      }
      if (axiosError.message) {
        return axiosError.message
      }
    }
    return 'An unexpected error occurred'
  }

  return {
    // State
    apps,
    listApps,
    selectedApp,
    isLoading,
    error,
    filters,
    // Computed
    filteredApps,
    publishedApps,
    draftApps,
    appCount,
    publishedCount,
    draftCount,
    hasApps,
    // Actions
    fetchApps,
    fetchApp,
    createApp,
    updateApp,
    deleteApp,
    publishApp,
    unpublishApp,
    duplicateApp,
    forkTemplate,
    importApp,
    setSearchFilter,
    setStatusFilter,
    setProjectFilter,
    clearFilters,
    clearError,
    reset,
  }
})
