import { ref, watch, onMounted } from 'vue'

const isDark = ref(false)

export function useTheme() {
  const toggleDarkMode = () => {
    isDark.value = !isDark.value
  }

  const setDarkMode = (value: boolean) => {
    isDark.value = value
  }

  // Watch for changes and update DOM
  watch(
    isDark,
    (newValue) => {
      if (newValue) {
        document.documentElement.classList.add('app-dark')
      } else {
        document.documentElement.classList.remove('app-dark')
      }
      // Persist preference
      localStorage.setItem('theme', newValue ? 'dark' : 'light')
    },
    { immediate: true }
  )

  // Initialize from localStorage or system preference
  onMounted(() => {
    const savedTheme = localStorage.getItem('theme')
    if (savedTheme) {
      isDark.value = savedTheme === 'dark'
    } else if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
      isDark.value = true
    }
  })

  return {
    isDark,
    toggleDarkMode,
    setDarkMode,
  }
}
