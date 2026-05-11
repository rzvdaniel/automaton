import api from '@/shared/services/api/axios-instance'

export interface CheckoutResponse {
  checkoutUrl: string
}

export interface PortalResponse {
  portalUrl: string
}

export interface BillingStatusResponse {
  planName: string
  priceUsd: number
  hasActiveSubscription: boolean
  capabilities: PlanCapabilities
}

export interface PlanCapabilities {
  version: number
  hostedRunner: HostedRunnerCapability
}

export interface HostedRunnerCapability {
  enabled: boolean
  cpuCores: number
  memoryMb: number
  diskMb: number
  maxApps: number
  visibility: 'disabled' | 'testUsersOnly' | 'public'
}

const billingService = {
  async createCheckoutSession(planId: string): Promise<CheckoutResponse> {
    const { data } = await api.post<CheckoutResponse>(`/api/billing/checkout?planId=${planId}`)
    return data
  },

  async createPortalSession(): Promise<PortalResponse> {
    const { data } = await api.post<PortalResponse>('/api/billing/portal')
    return data
  },

  async getBillingStatus(): Promise<BillingStatusResponse> {
    const { data } = await api.get<BillingStatusResponse>('/api/billing/status')
    return data
  },
}

export default billingService
