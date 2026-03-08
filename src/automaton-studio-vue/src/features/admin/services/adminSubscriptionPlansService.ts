/**
 * Admin Subscription Plans Service
 * Handles API calls for managing subscription plans and user plan assignments.
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  SubscriptionPlan,
  CreateSubscriptionPlanRequest,
  UpdateSubscriptionPlanRequest,
  UserPlan,
  AssignPlanRequest,
} from '../types/admin.types'

class AdminSubscriptionPlansService {
  // ── Plans ────────────────────────────────────────────────────────────────

  async getPlans(): Promise<SubscriptionPlan[]> {
    const response = await apiClient.get<SubscriptionPlan[]>('/api/admin/subscription-plans')
    return response.data
  }

  async createPlan(req: CreateSubscriptionPlanRequest): Promise<SubscriptionPlan> {
    const response = await apiClient.post<SubscriptionPlan>('/api/admin/subscription-plans', req)
    return response.data
  }

  async updatePlan(id: string, req: UpdateSubscriptionPlanRequest): Promise<SubscriptionPlan> {
    const response = await apiClient.put<SubscriptionPlan>(`/api/admin/subscription-plans/${id}`, req)
    return response.data
  }

  async deletePlan(id: string): Promise<void> {
    await apiClient.delete(`/api/admin/subscription-plans/${id}`)
  }

  // ── Users ────────────────────────────────────────────────────────────────

  async getUsers(): Promise<UserPlan[]> {
    const response = await apiClient.get<UserPlan[]>('/api/admin/subscription-plans/users')
    return response.data
  }

  async assignPlan(userId: string, req: AssignPlanRequest): Promise<void> {
    await apiClient.post(`/api/admin/subscription-plans/users/${userId}/assign`, req)
  }
}

export const adminSubscriptionPlansService = new AdminSubscriptionPlansService()
