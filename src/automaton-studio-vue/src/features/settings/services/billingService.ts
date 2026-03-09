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
  monthlyCostLimitUsd: number
  currentMonthCostUsd: number
  stripeSubscriptionId: string | null
  isActive: boolean
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
