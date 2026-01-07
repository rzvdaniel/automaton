/**
 * Auth service for API calls
 * Connects to ASP.NET backend endpoints
 */
import apiClient, { tokenStorage } from '@/shared/services/api/axios-instance'
import type {
  LoginRequest,
  RegisterRequest,
  RefreshTokenRequest,
  ForgotPasswordRequest,
  ResetPasswordRequest,
  ChangePasswordRequest,
  JsonWebToken,
  User,
} from '../types/auth.types'

class AuthService {
  /**
   * Login user with email and password
   * POST /api/Account/LoginUser
   */
  async login(request: LoginRequest): Promise<JsonWebToken> {
    const { data } = await apiClient.post<JsonWebToken>('/api/Account/LoginUser', request)

    // Store tokens
    tokenStorage.setAccessToken(data.accessToken)
    tokenStorage.setRefreshToken(data.refreshToken)

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
  async refreshToken(request: RefreshTokenRequest): Promise<JsonWebToken> {
    const { data } = await apiClient.post<JsonWebToken>('/api/Token/RefreshAccessToken', request)

    // Store new tokens
    tokenStorage.setAccessToken(data.accessToken)
    tokenStorage.setRefreshToken(data.refreshToken)

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

  /**
   * Logout - clear stored tokens
   */
  logout(): void {
    tokenStorage.clearTokens()
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

  /**
   * Get stored refresh token
   */
  getStoredRefreshToken(): string | null {
    return tokenStorage.getRefreshToken()
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
