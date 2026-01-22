/**
 * Auth Pinia store
 * Manages authentication state, user data, and tokens
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authService } from '../services/auth.service'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import type { User, LoginRequest, RegisterRequest } from '../types/auth.types'

export const useAuthStore = defineStore('auth', () => {
  // State
  const user = ref<User | null>(null)
  const accessToken = ref<string | null>(authService.getStoredAccessToken())
  const refreshToken = ref<string | null>(authService.getStoredRefreshToken())
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const isInitialized = ref(false)

  // Computed
  const isAuthenticated = computed(() => !!accessToken.value && !!user.value)
  const userName = computed(() => user.value?.name ?? '')
  const userEmail = computed(() => user.value?.email ?? '')
  const userId = computed(() => user.value?.id ?? '')
  const publicUsername = computed(() => user.value?.publicUsername ?? '')

  // Actions
  async function login(request: LoginRequest): Promise<void> {
    isLoading.value = true
    error.value = null

    try {
      const token = await authService.login(request)
      accessToken.value = token.accessToken
      refreshToken.value = token.refreshToken

      // Fetch user profile after login
      await fetchUserProfile()

      // Initialize user settings from server after successful login
      const { initializeSettings } = useUserSettings()
      await initializeSettings()
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      error.value = errorMessage
      throw new Error(errorMessage)
    } finally {
      isLoading.value = false
    }
  }

  async function register(request: RegisterRequest): Promise<void> {
    isLoading.value = true
    error.value = null

    try {
      await authService.register(request)
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      error.value = errorMessage
      throw new Error(errorMessage)
    } finally {
      isLoading.value = false
    }
  }

  async function fetchUserProfile(): Promise<void> {
    if (!accessToken.value) return

    try {
      user.value = await authService.getUserProfile()
    } catch (err: unknown) {
      // If profile fetch fails, user might not be authenticated
      console.error('Failed to fetch user profile:', err)
      user.value = null
    }
  }

  async function initialize(): Promise<void> {
    if (isInitialized.value) return

    // Check if we have a stored token
    if (authService.hasStoredToken()) {
      accessToken.value = authService.getStoredAccessToken()
      refreshToken.value = authService.getStoredRefreshToken()

      // Try to fetch user profile to validate token
      try {
        await fetchUserProfile()

        // Initialize user settings from server after successful auth check
        const { initializeSettings } = useUserSettings()
        await initializeSettings()
      } catch {
        // Token might be invalid, clear it
        logout()
      }
    }

    isInitialized.value = true
  }

  function logout(): void {
    authService.logout()
    user.value = null
    accessToken.value = null
    refreshToken.value = null
    error.value = null

    // Reset user settings to defaults on logout
    const { resetSettings } = useUserSettings()
    resetSettings()
  }

  function clearError(): void {
    error.value = null
  }

  function setPublicUsername(newPublicUsername: string): void {
    if (user.value) {
      user.value.publicUsername = newPublicUsername
    }
  }

  // Helper to extract error message
  function getErrorMessage(err: unknown): string {
    if (err && typeof err === 'object') {
      // Check for Axios error response
      const axiosError = err as { response?: { data?: { message?: string; title?: string }; status?: number } }
      if (axiosError.response?.data?.message) {
        return axiosError.response.data.message
      }
      if (axiosError.response?.data?.title) {
        return axiosError.response.data.title
      }
      if (axiosError.response?.status === 401) {
        return 'Invalid email or password'
      }
      if (axiosError.response?.status === 400) {
        return 'Invalid request. Please check your input.'
      }
      if (axiosError.response?.status === 404) {
        return 'Registration is currently disabled.'
      }
      // Check for standard Error
      if ('message' in err && typeof (err as Error).message === 'string') {
        return (err as Error).message
      }
    }
    return 'An unexpected error occurred'
  }

  return {
    // State
    user,
    accessToken,
    refreshToken,
    isLoading,
    error,
    isInitialized,
    // Computed
    isAuthenticated,
    userName,
    userEmail,
    userId,
    publicUsername,
    // Actions
    login,
    register,
    logout,
    initialize,
    fetchUserProfile,
    clearError,
    setPublicUsername,
  }
})
