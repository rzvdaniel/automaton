import { ref, readonly, computed } from 'vue'
import { userSettingsService } from '@/features/settings/services/userSettingsService'
import type { EditorSettings } from '@/features/settings/types/settings.types'

// Default values
const DEFAULTS: EditorSettings = {
  fontSize: 14,
}

const STORAGE_KEY = 'editor-settings'

// Global state - shared across all components using this composable
const settings = ref<EditorSettings>({ ...DEFAULTS })
const isInitialized = ref(false)
const isLoading = ref(false)
const isSaving = ref(false)

// Load from localStorage (sync, for immediate use)
function loadFromStorage(): EditorSettings {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      const parsed = JSON.parse(stored) as Partial<EditorSettings>
      return {
        fontSize: parsed.fontSize ?? DEFAULTS.fontSize,
      }
    }
  } catch (err) {
    console.warn('Failed to load editor settings from storage:', err)
  }
  return { ...DEFAULTS }
}

// Save to localStorage
function saveToStorage(value: EditorSettings) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(value))
  } catch (err) {
    console.warn('Failed to save editor settings to storage:', err)
  }
}

// Initialize from storage immediately (before server fetch)
settings.value = loadFromStorage()

export function useEditorSettings() {
  /**
   * Initialize settings from server (call once after user is authenticated)
   * Syncs server settings to local state and storage
   */
  async function initializeSettings() {
    if (isInitialized.value || isLoading.value) return

    isLoading.value = true
    try {
      const serverSettings = await userSettingsService.getEditorSettings()
      settings.value = serverSettings
      saveToStorage(serverSettings)
      isInitialized.value = true
    } catch (err) {
      console.error('Failed to load editor settings from server:', err)
      // Keep localStorage/default values
      isInitialized.value = true
    } finally {
      isLoading.value = false
    }
  }

  /**
   * Update font size and persist to server
   */
  async function setFontSize(fontSize: number) {
    // Validate range
    const validSize = Math.min(24, Math.max(10, fontSize))
    const previousSettings = { ...settings.value }

    // Optimistic update
    settings.value = { ...settings.value, fontSize: validSize }
    saveToStorage(settings.value)

    // Persist to server
    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateEditorSettings({ fontSize: validSize })
      } catch (err) {
        console.error('Failed to save editor settings:', err)
        // Revert on failure
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  /**
   * Reset settings state (call on logout)
   */
  function resetSettings() {
    isInitialized.value = false
    settings.value = { ...DEFAULTS }
    localStorage.removeItem(STORAGE_KEY)
  }

  // Computed for convenience access
  const fontSize = computed(() => settings.value.fontSize)

  return {
    // Read-only reactive settings
    settings: readonly(settings),
    fontSize,

    // State
    isInitialized: readonly(isInitialized),
    isLoading: readonly(isLoading),
    isSaving: readonly(isSaving),

    // Actions
    initializeSettings,
    setFontSize,
    resetSettings,
  }
}
