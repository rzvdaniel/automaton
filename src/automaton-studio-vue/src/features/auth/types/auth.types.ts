/**
 * Auth feature TypeScript types
 * Based on ASP.NET API contracts:
 * - POST /api/Account/LoginUser
 * - POST /api/Account/RegisterUser
 * - POST /api/Token/RefreshAccessToken
 * - GET /api/User/GetUserProfile
 */

// Request types
export interface LoginRequest {
  email: string
  password: string
}

export interface RegisterRequest {
  name: string
  email: string
  password: string
}

export interface RefreshTokenRequest {
  token: string
}

// Response types
export interface JsonWebToken {
  accessToken: string
  refreshToken: string
  expires: number
  userId: string
}

export interface User {
  id: string
  name: string
  email: string
}

// Auth state
export interface AuthState {
  user: User | null
  accessToken: string | null
  refreshToken: string | null
  tokenExpires: number | null
  isAuthenticated: boolean
  isLoading: boolean
  error: string | null
}

// Form validation types
export interface LoginFormErrors {
  email?: string
  password?: string
}

export interface RegisterFormErrors {
  name?: string
  email?: string
  password?: string
  confirmPassword?: string
  terms?: string
}

// Password-related request types
export interface ForgotPasswordRequest {
  email: string
}

export interface ResetPasswordRequest {
  email: string
  password: string
  code: string
}

export interface ChangePasswordRequest {
  oldPassword: string
  newPassword: string
}

// Password form validation types
export interface ForgotPasswordFormErrors {
  email?: string
}

export interface ResetPasswordFormErrors {
  password?: string
  confirmPassword?: string
}

export interface ChangePasswordFormErrors {
  oldPassword?: string
  newPassword?: string
  confirmPassword?: string
}

// API error response
export interface ApiError {
  message: string
  errors?: Record<string, string[]>
  status?: number
}
