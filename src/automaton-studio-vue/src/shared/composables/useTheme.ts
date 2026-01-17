import { ref, computed, watch } from 'vue'
import { userSettingsService } from '@/features/settings/services/userSettingsService'
import type { ThemeMode } from '@/features/settings/types/settings.types'

// Global state - shared across all components using this composable
const themePreference = ref<ThemeMode>('system')
const systemPrefersDark = ref(false)
const isInitialized = ref(false)
const isSaving = ref(false)

// Computed: actual dark mode based on preference and system
const isDark = computed(() => {
  if (themePreference.value === 'system') {
    return systemPrefersDark.value
  }
  return themePreference.value === 'dark'
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

// Listen to system preference changes
if (typeof window !== 'undefined' && window.matchMedia) {
  const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
  systemPrefersDark.value = mediaQuery.matches

  mediaQuery.addEventListener('change', (e) => {
    systemPrefersDark.value = e.matches
  })
}

export function useTheme() {
  /**
   * Initialize theme from server (call once on app mount when user is authenticated)
   */
  async function initializeTheme() {
    if (isInitialized.value) return

    try {
      const settings = await userSettingsService.getAppearanceSettings()
      themePreference.value = settings.theme as ThemeMode
      isInitialized.value = true
    } catch (err) {
      console.error('Failed to load theme settings:', err)
      // Fall back to localStorage or system
      const savedTheme = localStorage.getItem('theme') as ThemeMode | null
      if (savedTheme && ['light', 'dark', 'system'].includes(savedTheme)) {
        themePreference.value = savedTheme
      }
      isInitialized.value = true
    }
  }

  /**
   * Set theme preference and persist to server
   */
  async function setTheme(theme: ThemeMode) {
    const previousTheme = themePreference.value
    themePreference.value = theme

    // Also save to localStorage as fallback
    localStorage.setItem('theme', theme)

    // Persist to server
    if (!isSaving.value) {
      isSaving.value = true
      try {
        await userSettingsService.updateAppearanceSettings({ theme })
      } catch (err) {
        console.error('Failed to save theme setting:', err)
        // Revert on failure
        themePreference.value = previousTheme
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
   * Reset theme state (call on logout)
   */
  function resetTheme() {
    isInitialized.value = false
    themePreference.value = 'system'
    localStorage.removeItem('theme')
  }

  return {
    isDark,
    themePreference,
    isInitialized,
    isSaving,
    toggleDarkMode,
    setDarkMode,
    setTheme,
    initializeTheme,
    resetTheme,
  }
}
