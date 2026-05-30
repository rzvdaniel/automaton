/**
 * Auth composable
 * Provides convenient access to auth state and actions
 */
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth.store'
import type { LoginRequest, RegisterRequest } from '../types/auth.types'

export function useAuth() {
  const store = useAuthStore()
  const router = useRouter()

  // Computed properties for easy access
  const isAuthenticated = computed(() => store.isAuthenticated)
  const isLoading = computed(() => store.isLoading)
  const user = computed(() => store.user)
  const userName = computed(() => store.userName)
  const userEmail = computed(() => store.userEmail)
  const isEmailConfirmed = computed(() => store.isEmailConfirmed)
  const error = computed(() => store.error)

  /**
   * Login and redirect to dashboard
   */
  async function login(request: LoginRequest, redirectTo: string = '/'): Promise<boolean> {
    try {
      await store.login(request)
      router.push(redirectTo)
      return true
    } catch {
      return false
    }
  }

  /**
   * Register and redirect to login
   */
  async function register(request: RegisterRequest): Promise<boolean> {
    try {
      await store.register(request)
      return true
    } catch {
      return false
    }
  }

  /**
   * Logout and redirect to login page
   */
  async function logout(redirectTo: string = '/login'): Promise<void> {
    await store.logout()
    await router.push(redirectTo)
  }

  /**
   * Clear any auth errors
   */
  function clearError(): void {
    store.clearError()
  }

  /**
   * Initialize auth state (check for stored tokens)
   */
  async function initialize(): Promise<void> {
    await store.initialize()
  }

  async function resendConfirmation(): Promise<void> {
    await store.resendConfirmation()
  }

  return {
    // State
    isAuthenticated,
    isLoading,
    user,
    userName,
    userEmail,
    isEmailConfirmed,
    error,
    // Actions
    login,
    register,
    logout,
    clearError,
    initialize,
    resendConfirmation,
  }
}
