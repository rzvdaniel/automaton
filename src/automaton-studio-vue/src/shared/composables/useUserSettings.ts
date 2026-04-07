import { ref, readonly, computed, watch } from 'vue'
import { userSettingsService } from '@/features/settings/services/userSettingsService'
import type { ThemeMode, UserSettingsDto } from '@/features/settings/types/settings.types'

// Default values
const DEFAULTS: UserSettingsDto = {
  theme: 'system',
  fontSize: 14,
  defaultAiModel: undefined,
  defaultAiProvider: undefined,
  defaultPlanningModel: undefined,
  defaultPlanningProvider: undefined,
  showAiDebugDetails: false,
  appVerificationStrategy: undefined,
  appExecutionPolicy: undefined,
}

const STORAGE_KEY = 'user-settings'

// Global state - shared across all components using this composable
const settings = ref<UserSettingsDto>({ ...DEFAULTS })
const isInitialized = ref(false)
const isLoading = ref(false)
const isSaving = ref(false)

// System preference detection
const systemPrefersDark = ref(false)

// Listen to system preference changes
if (typeof window !== 'undefined' && window.matchMedia) {
  const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
  systemPrefersDark.value = mediaQuery.matches

  mediaQuery.addEventListener('change', (e) => {
    systemPrefersDark.value = e.matches
  })
}

// Computed: actual dark mode based on preference and system
const isDark = computed(() => {
  if (settings.value.theme === 'system') {
    return systemPrefersDark.value
  }
  return settings.value.theme === 'dark'
})

// Apply theme to DOM
function applyTheme(dark: boolean) {
  if (dark) {
    document.documentElement.classList.add('app-dark')
  } else {
    document.documentElement.classList.remove('app-dark')
  }
}

// Watch for theme changes and apply to DOM
watch(isDark, (newValue) => {
  applyTheme(newValue)
}, { immediate: true })

// Load from localStorage (sync, for immediate use)
function loadFromStorage(): UserSettingsDto {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      const parsed = JSON.parse(stored) as Partial<UserSettingsDto>
      return {
        theme: parsed.theme && ['light', 'dark', 'system'].includes(parsed.theme)
          ? parsed.theme
          : DEFAULTS.theme,
        fontSize: parsed.fontSize ?? DEFAULTS.fontSize,
        defaultAiModel: parsed.defaultAiModel ?? undefined,
        defaultAiProvider: parsed.defaultAiProvider ?? undefined,
        defaultPlanningModel: parsed.defaultPlanningModel ?? undefined,
        defaultPlanningProvider: parsed.defaultPlanningProvider ?? undefined,
        showAiDebugDetails: parsed.showAiDebugDetails ?? false,
        appVerificationStrategy: parsed.appVerificationStrategy ?? undefined,
        appExecutionPolicy: parsed.appExecutionPolicy ?? undefined,
      }
    }
  } catch (err) {
    console.warn('Failed to load user settings from storage:', err)
  }
  return { ...DEFAULTS }
}

// Save to localStorage
function saveToStorage(value: UserSettingsDto) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(value))
  } catch (err) {
    console.warn('Failed to save user settings to storage:', err)
  }
}

// Initialize from storage immediately (before server fetch)
settings.value = loadFromStorage()

export function useUserSettings() {
  /**
   * Initialize settings from server (call once after user is authenticated)
   * Fetches all settings in one call and syncs to local state and storage
   */
  async function initializeSettings() {
    if (isInitialized.value || isLoading.value) return

    isLoading.value = true
    try {
      // Fetch all settings in one call
      const serverSettings = await userSettingsService.getSettings()

      settings.value = {
        theme: serverSettings.theme,
        fontSize: serverSettings.fontSize,
        defaultAiModel: serverSettings.defaultAiModel ?? undefined,
        defaultAiProvider: serverSettings.defaultAiProvider ?? undefined,
        defaultPlanningModel: serverSettings.defaultPlanningModel ?? undefined,
        defaultPlanningProvider: serverSettings.defaultPlanningProvider ?? undefined,
        showAiDebugDetails: serverSettings.showAiDebugDetails ?? false,
        appVerificationStrategy: serverSettings.appVerificationStrategy ?? undefined,
        appExecutionPolicy: serverSettings.appExecutionPolicy ?? undefined,
      }
      saveToStorage(settings.value)
      isInitialized.value = true
    } catch (err) {
      console.error('Failed to load user settings from server:', err)
      // Keep localStorage/default values
      isInitialized.value = true
    } finally {
      isLoading.value = false
    }
  }

  /**
   * Set theme preference and persist to server
   */
  async function setTheme(theme: ThemeMode) {
    const previousSettings = { ...settings.value }

    // Optimistic update
    settings.value = { ...settings.value, theme }
    saveToStorage(settings.value)

    // Persist to server (partial update)
    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({ theme })
      } catch (err) {
        console.error('Failed to save theme setting:', err)
        // Revert on failure
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  /**
   * Toggle between light and dark (ignores system)
   */
  function toggleDarkMode() {
    const newTheme = isDark.value ? 'light' : 'dark'
    setTheme(newTheme)
  }

  /**
   * Set dark mode directly (for compatibility)
   */
  function setDarkMode(value: boolean) {
    setTheme(value ? 'dark' : 'light')
  }

  /**
   * Set font size and persist to server
   */
  async function setFontSize(fontSize: number) {
    // Validate range
    const validSize = Math.min(24, Math.max(10, fontSize))
    const previousSettings = { ...settings.value }

    // Optimistic update
    settings.value = { ...settings.value, fontSize: validSize }
    saveToStorage(settings.value)

    // Persist to server (partial update)
    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({ fontSize: validSize })
      } catch (err) {
        console.error('Failed to save font size setting:', err)
        // Revert on failure
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  /**
   * Set default AI model preference and persist to server
   */
  async function setDefaultAiModel(model: string | undefined, provider: string | undefined) {
    const previousSettings = { ...settings.value }

    settings.value = { ...settings.value, defaultAiModel: model, defaultAiProvider: provider }
    saveToStorage(settings.value)

    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({
          // Send explicit clear markers so backend can clear persisted values.
          defaultAiModel: model ?? '',
          defaultAiProvider: provider ?? '',
        })
      } catch (err) {
        console.error('Failed to save default AI model setting:', err)
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  /**
   * Set default planning model preference and persist to server
   */
  async function setDefaultPlanningModel(model: string | undefined, provider: string | undefined) {
    const previousSettings = { ...settings.value }

    settings.value = { ...settings.value, defaultPlanningModel: model, defaultPlanningProvider: provider }
    saveToStorage(settings.value)

    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({
          // Send explicit clear markers so backend can clear persisted values.
          defaultPlanningModel: model ?? '',
          defaultPlanningProvider: provider ?? '',
        })
      } catch (err) {
        console.error('Failed to save default planning model setting:', err)
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  async function setShowAiDebugDetails(enabled: boolean) {
    const previousSettings = { ...settings.value }

    settings.value = { ...settings.value, showAiDebugDetails: enabled }
    saveToStorage(settings.value)

    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({
          showAiDebugDetails: enabled,
        })
      } catch (err) {
        console.error('Failed to save AI debug detail setting:', err)
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  async function setAppVerificationStrategy(strategy: string | undefined) {
    const previousSettings = { ...settings.value }

    settings.value = { ...settings.value, appVerificationStrategy: strategy }
    saveToStorage(settings.value)

    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({
          appVerificationStrategy: strategy ?? '',
        })
      } catch (err) {
        console.error('Failed to save app verification strategy setting:', err)
        settings.value = previousSettings
        saveToStorage(previousSettings)
      } finally {
        isSaving.value = false
      }
    }
  }

  async function setAppExecutionPolicy(policy: string | undefined) {
    const previousSettings = { ...settings.value }

    settings.value = { ...settings.value, appExecutionPolicy: policy }
    saveToStorage(settings.value)

    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateSettings({
          appExecutionPolicy: policy ?? '',
        })
      } catch (err) {
        console.error('Failed to save app execution policy setting:', err)
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

  // Computed convenience accessors
  const theme = computed(() => settings.value.theme)
  const fontSize = computed(() => settings.value.fontSize)
  const defaultAiModel = computed(() => settings.value.defaultAiModel)
  const defaultAiProvider = computed(() => settings.value.defaultAiProvider)
  const defaultPlanningModel = computed(() => settings.value.defaultPlanningModel)
  const defaultPlanningProvider = computed(() => settings.value.defaultPlanningProvider)
  const showAiDebugDetails = computed(() => settings.value.showAiDebugDetails ?? false)
  const appVerificationStrategy = computed(() => settings.value.appVerificationStrategy)
  const appExecutionPolicy = computed(() => settings.value.appExecutionPolicy)

  return {
    // Read-only reactive settings object
    settings: readonly(settings),

    // Computed convenience accessors
    theme,
    fontSize,
    isDark,
    defaultAiModel,
    defaultAiProvider,
    defaultPlanningModel,
    defaultPlanningProvider,
    showAiDebugDetails,
    appVerificationStrategy,
    appExecutionPolicy,

    // State
    isInitialized: readonly(isInitialized),
    isLoading: readonly(isLoading),
    isSaving: readonly(isSaving),

    // Actions
    initializeSettings,
    setTheme,
    setFontSize,
    setDefaultAiModel,
    setDefaultPlanningModel,
    setShowAiDebugDetails,
    setAppVerificationStrategy,
    setAppExecutionPolicy,
    toggleDarkMode,
    setDarkMode,
    resetSettings,
  }
}
