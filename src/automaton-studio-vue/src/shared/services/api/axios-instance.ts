/**
 * Axios instance configuration with JWT interceptors
 * - Adds Authorization header to all authenticated requests
 * - Handles token refresh on 401 responses
 */
import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'

// Logout callback - will be set by the app
let onUnauthorized: (() => void) | null = null
let accessToken: string | null = null

// Create axios instance
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL,
  timeout: 30000,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
})

/**
 * Set callback to handle unauthorized access (401 after failed refresh)
 * This allows the app to handle logout via router instead of window.location
 */
export const setUnauthorizedCallback = (callback: () => void): void => {
  onUnauthorized = callback
}

// Token storage helpers
export const tokenStorage = {
  getAccessToken: (): string | null => accessToken,
  setAccessToken: (token: string | null): void => {
    accessToken = token
  },
  clearTokens: (): void => {
    accessToken = null
  },
}

// Flag to prevent multiple refresh attempts
let isRefreshing = false
let failedQueue: Array<{
  resolve: (token: string) => void
  reject: (error: unknown) => void
}> = []

const processQueue = (error: unknown, token: string | null = null) => {
  failedQueue.forEach((prom) => {
    if (error) {
      prom.reject(error)
    } else {
      prom.resolve(token!)
    }
  })
  failedQueue = []
}

// Request interceptor - Add JWT token to requests
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = tokenStorage.getAccessToken()
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  },
  (error) => Promise.reject(error),
)

// Response interceptor - Handle 401 and refresh token
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean }

    // If error is 401 and we haven't already tried to refresh
    if (error.response?.status === 401 && !originalRequest._retry) {
      // Don't try to refresh if we're on auth routes
      const isAuthRoute =
        originalRequest.url?.includes('/Account/LoginUser') ||
        originalRequest.url?.includes('/Account/RegisterUser') ||
        originalRequest.url?.includes('/Token/RefreshAccessToken')

      if (isAuthRoute) {
        return Promise.reject(error)
      }

      if (isRefreshing) {
        // Wait for the refresh to complete
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject })
        })
          .then((token) => {
            if (originalRequest.headers) {
              originalRequest.headers.Authorization = `Bearer ${token}`
            }
            return apiClient(originalRequest)
          })
          .catch((err) => Promise.reject(err))
      }

      originalRequest._retry = true
      isRefreshing = true

      try {
        // Call refresh token endpoint
        const response = await axios.post(
          `${import.meta.env.VITE_API_BASE_URL}/api/Token/RefreshAccessToken`,
          {},
          {
            withCredentials: true,
            headers: { 'Content-Type': 'application/json' },
          },
        )

        const { accessToken } = response.data

        tokenStorage.setAccessToken(accessToken)

        processQueue(null, accessToken)

        if (originalRequest.headers) {
          originalRequest.headers.Authorization = `Bearer ${accessToken}`
        }

        return apiClient(originalRequest)
      } catch (refreshError) {
        processQueue(refreshError, null)
        tokenStorage.clearTokens()
        // Use callback instead of window.location to avoid page reload
        if (onUnauthorized) {
          onUnauthorized()
        }
        return Promise.reject(refreshError)
      } finally {
        isRefreshing = false
      }
    }

    return Promise.reject(error)
  },
)

export default apiClient
