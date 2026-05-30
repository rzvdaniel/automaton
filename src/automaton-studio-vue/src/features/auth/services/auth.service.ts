/**
 * Auth service for API calls
 * Connects to ASP.NET backend endpoints
 */
import apiClient, { tokenStorage } from '@/shared/services/api/axios-instance'
import type {
  LoginRequest,
  RegisterRequest,
  ForgotPasswordRequest,
  ResetPasswordRequest,
  ChangePasswordRequest,
  JsonWebToken,
  User,
} from '../types/auth.types'

class AuthService {
  private getApiBaseUrl(): string {
    return (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/$/, '')
  }

  private getGitHubAuthBaseUrl(): string {
    return (import.meta.env.VITE_GITHUB_AUTH_API_BASE_URL || this.getApiBaseUrl()).replace(/\/$/, '')
  }

  /**
   * Login user with email and password
   * POST /api/Account/LoginUser
   */
  async login(request: LoginRequest): Promise<JsonWebToken> {
    const { data } = await apiClient.post<JsonWebToken>('/api/Account/LoginUser', request)

    // Store access token in memory only. Refresh token is held in an HttpOnly cookie.
    tokenStorage.setAccessToken(data.accessToken)

    return data
  }

  /**
   * Register a new user
   * POST /api/Account/RegisterUser
   */
  async register(request: RegisterRequest): Promise<string> {
    const { data } = await apiClient.post<string>('/api/Account/RegisterUser', request)
    return data
  }

  /**
   * Refresh access token
   * POST /api/Token/RefreshAccessToken
   */
  async refreshToken(): Promise<JsonWebToken> {
    const { data } = await apiClient.post<JsonWebToken>('/api/Token/RefreshAccessToken', {})

    // Store access token in memory only. Refresh token is rotated in an HttpOnly cookie.
    tokenStorage.setAccessToken(data.accessToken)

    return data
  }

  /**
   * Get current user profile
   * GET /api/User/GetUserProfile
   */
  async getUserProfile(): Promise<User> {
    const { data } = await apiClient.get<User>('/api/User/GetUserProfile')
    return data
  }

  async resendConfirmation(): Promise<void> {
    await apiClient.post('/api/Account/ResendConfirmation')
  }

  /**
   * Logout - clear stored tokens
   */
  async logout(): Promise<void> {
    try {
      await apiClient.post('/api/Token/Logout', {})
    } catch {
      // Best effort - still clear local auth state even if revocation request fails.
    }

    tokenStorage.clearTokens()
  }

  storeAccessToken(accessToken: string): void {
    tokenStorage.setAccessToken(accessToken)
  }

  startGitHubLogin(returnUrl?: string): void {
    const authBaseUrl = this.getGitHubAuthBaseUrl()
    const loginUrl = new URL(`${authBaseUrl}/auth/github/login`)

    if (returnUrl) {
      loginUrl.searchParams.set('returnUrl', returnUrl)
    }

    window.location.assign(loginUrl.toString())
  }

  /**
   * Check if user has a stored token
   */
  hasStoredToken(): boolean {
    return !!tokenStorage.getAccessToken()
  }

  /**
   * Get stored access token
   */
  getStoredAccessToken(): string | null {
    return tokenStorage.getAccessToken()
  }

  async restoreSession(): Promise<JsonWebToken> {
    return this.refreshToken()
  }

  /**
   * Check if a public username is available
   * GET /api/Account/IsPublicUsernameAvailable/{publicUsername}
   */
  async checkPublicUsernameAvailable(publicUsername: string): Promise<boolean> {
    const { data } = await apiClient.get<boolean>(`/api/Account/IsPublicUsernameAvailable/${publicUsername}`)
    return data
  }

  /**
   * Update public username
   * PUT /api/Account/UpdatePublicUsername
   */
  async updatePublicUsername(publicUsername: string): Promise<void> {
    await apiClient.put('/api/Account/UpdatePublicUsername', {
      newPublicUsername: publicUsername,
    })
  }

  /**
   * Request password reset email
   * POST /api/Account/ForgotUserPassword
   */
  async forgotPassword(request: ForgotPasswordRequest): Promise<void> {
    await apiClient.post('/api/Account/ForgotUserPassword', request)
  }

  /**
   * Reset password with code from email
   * POST /api/Account/ResetUserPassword
   */
  async resetPassword(request: ResetPasswordRequest): Promise<void> {
    await apiClient.post('/api/Account/ResetUserPassword', request)
  }

  /**
   * Change password for authenticated user
   * PUT /api/Account/UpdateUserPassword
   */
  async changePassword(request: ChangePasswordRequest): Promise<void> {
    await apiClient.put('/api/Account/UpdateUserPassword', request)
  }
}

export const authService = new AuthService()
