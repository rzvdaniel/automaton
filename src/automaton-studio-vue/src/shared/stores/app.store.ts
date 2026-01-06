import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export type ThemeName = 'automatonLight' | 'automatonDark'

export const useAppStore = defineStore('app', () => {
  // State
  const drawer = ref(true)
  const rail = ref(true) // Start collapsed per design spec
  const theme = ref<ThemeName>('automatonLight')
  const loading = ref(false)
  const snackbar = ref({
    show: false,
    message: '',
    color: 'success' as 'success' | 'error' | 'warning' | 'info',
    timeout: 3000,
  })

  // Getters
  const isDark = computed(() => theme.value === 'automatonDark')

  // Actions
  function toggleDrawer() {
    drawer.value = !drawer.value
  }

  function toggleRail() {
    rail.value = !rail.value
  }

  function toggleTheme() {
    theme.value = theme.value === 'automatonLight' ? 'automatonDark' : 'automatonLight'
    localStorage.setItem('automaton-theme', theme.value)
  }

  function setTheme(newTheme: ThemeName) {
    theme.value = newTheme
    localStorage.setItem('automaton-theme', newTheme)
  }

  function initTheme() {
    const savedTheme = localStorage.getItem('automaton-theme') as ThemeName | null
    if (savedTheme && (savedTheme === 'automatonLight' || savedTheme === 'automatonDark')) {
      theme.value = savedTheme
    } else if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
      theme.value = 'automatonDark'
    }
  }

  function setLoading(value: boolean) {
    loading.value = value
  }

  function showSnackbar(
    message: string,
    color: 'success' | 'error' | 'warning' | 'info' = 'success',
    timeout = 3000
  ) {
    snackbar.value = {
      show: true,
      message,
      color,
      timeout,
    }
  }

  function hideSnackbar() {
    snackbar.value.show = false
  }

  return {
    // State
    drawer,
    rail,
    theme,
    loading,
    snackbar,
    // Getters
    isDark,
    // Actions
    toggleDrawer,
    toggleRail,
    toggleTheme,
    setTheme,
    initTheme,
    setLoading,
    showSnackbar,
    hideSnackbar,
  }
})
